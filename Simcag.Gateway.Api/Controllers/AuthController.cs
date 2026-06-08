using Microsoft.AspNetCore.Mvc;
using Simcag.Shared.ErrorHandling;

namespace Simcag.Gateway.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly ILogger<AuthController> _logger;

    public AuthController(ILogger<AuthController> logger)
    {
        _logger = logger;
    }

    [HttpPost("login")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<ProblemDetails>> Login(AuthLoginRequest request)
    {
        var identityUrl = Environment.GetEnvironmentVariable("SERVICES__IDENTITY__URL") ?? "http://identity-service:8080";

        try
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            var content = new StringContent(System.Text.Json.JsonSerializer.Serialize(request), System.Text.Encoding.UTF8, "application/json");

            var response = await client.PostAsync($"{identityUrl}/api/auth/login", content);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Auth login failed for user {Email}", request.Email);
                return BadRequest(new ProblemDetails { Status = (int)response.StatusCode, Type = $"https://simc.ag/errors/auth/failure", Title = "Autenticação falhou", Detail = await response.Content.ReadAsStringAsync() });
            }

            _logger.LogInformation("Login completed for user: {Email}", request.Email);
            return Ok(await response.Content.ReadFromJsonAsync<ProblemDetails>()!);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login proxy call");
            return StatusCode(500, new ProblemDetails { Status = 500, Title = "Erro interno do sistema" });
        }
    }

    [HttpPost("register")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<ActionResult<ProblemDetails>> Register(AuthRegisterRequest request)
    {
        var identityUrl = Environment.GetEnvironmentVariable("SERVICES__IDENTITY__URL") ?? "http://identity-service:8080";

        try
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            var content = new StringContent(System.Text.Json.JsonSerializer.Serialize(request), System.Text.Encoding.UTF8, "application/json");

            var response = await client.PostAsync($"{identityUrl}/api/auth/register", content);

            if (response.StatusCode == System.Net.HttpStatusCode.Created)
            {
                _logger.LogInformation("User registered: {Email}", request.Email);
                return CreatedAtAction(nameof(Login), new { email = request.Email }, response.Content);
            }

            return BadRequest(await response.Content.ReadFromJsonAsync<ProblemDetails>()!);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during register proxy call");
            return StatusCode(500, new ProblemDetails { Status = 500, Title = "Erro interno do sistema" });
        }
    }

    [HttpGet("profile")]
    public async Task<ActionResult<ProblemDetails>> Profile()
    {
        if (!User.Identity!.IsAuthenticated)
            return Unauthorized(new ProblemDetails { Status = 401, Title = "Sessão não autenticada" });

        var identityUrl = Environment.GetEnvironmentVariable("SERVICES__IDENTITY__URL") ?? "http://identity-service:8080";

        try
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {JwtUtil.ReadToken(User)}");

            var response = await client.GetAsync($"{identityUrl}/api/auth/me");

            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden || response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return Unauthorized(new ProblemDetails { Status = 401, Title = "Sessão expirada" });

            return Ok(await response.Content.ReadFromJsonAsync<ProblemDetails>()!);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during profile proxy call");
            return StatusCode(500, new ProblemDetails { Status = 500, Title = "Erro interno do sistema" });
        }
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        if (!User.Identity!.IsAuthenticated) return NoContent();

        var identityUrl = Environment.GetEnvironmentVariable("SERVICES__IDENTITY__URL") ?? "http://identity-service:8080";

        try
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            await client.PostAsync($"{identityUrl}/api/auth/logout", null);
            _logger.LogInformation("User logged out successfully");
            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Logout proxy request failed gracefully");
            return NoContent();
        }
    }

    public record AuthLoginRequest(string Email, string Password, string TenantId);
    public record AuthRegisterRequest(string Email, string Password, string Name, string Role, string? TenantId);
}

internal static class JwtUtil { public static string? ReadToken(System.Security.Claims.ClaimsPrincipal user) => user.FindFirst("auth:token")?.Value ?? null; }
