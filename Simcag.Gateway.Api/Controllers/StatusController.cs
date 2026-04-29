using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Simcag.Gateway.Api.Controllers;

/// <summary>
/// Controller para health checks e status do serviço.
/// - GET /health  → 200 Healthy | 200 Degraded (downstream offline) | 503 Unhealthy (gateway em si com problema)
/// - GET /info    → metadados do serviço
/// </summary>
[ApiController]
[Route("")]
public class StatusController : ControllerBase
{
    private readonly HealthCheckService _healthCheckService;

    public StatusController(HealthCheckService healthCheckService)
    {
        _healthCheckService = healthCheckService;
    }

    /// <summary>
    /// Health check do gateway.
    /// Retorna 200 quando saudável ou degraded (downstreams offline).
    /// Retorna 503 apenas quando o próprio gateway está unhealthy.
    /// </summary>
    [HttpGet("health")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Health()
    {
        var report = await _healthCheckService.CheckHealthAsync();

        var entries = report.Entries.Select(e => new
        {
            name    = e.Key,
            status  = e.Value.Status.ToString(),
            message = e.Value.Description,
            tags    = e.Value.Tags
        });

        var body = new
        {
            status    = report.Status.ToString().ToLowerInvariant(),
            timestamp = DateTime.UtcNow,
            checks    = entries
        };

        // Degraded (algum downstream offline) ainda é operacional → 200
        // Unhealthy (problema interno do gateway) → 503
        return report.Status == HealthStatus.Unhealthy
            ? StatusCode(StatusCodes.Status503ServiceUnavailable, body)
            : Ok(body);
    }

    /// <summary>
    /// Metadados do serviço.
    /// </summary>
    [HttpGet("info")]
    public IActionResult Info()
    {
        return Ok(new
        {
            service     = "gateway-service",
            version     = "1.0.0",
            environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development",
            timestamp   = DateTime.UtcNow
        });
    }
}
