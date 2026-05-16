using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Simcag.Gateway.Application.Interfaces;
using Simcag.Gateway.Domain.Entities;
using Simcag.Gateway.Domain.ValueObjects;
using Simcag.Shared.Security;

namespace Simcag.Gateway.Infrastructure.Middleware;

public class AuthService : IAuthService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthService> _logger;
    private readonly IGatewayAccessEvaluator _access;

    public AuthService(
        IConfiguration configuration,
        ILogger<AuthService> logger,
        IGatewayAccessEvaluator access)
    {
        _configuration = configuration;
        _logger = logger;
        _access = access;
    }

    public Task<UserContext?> ValidateTokenAsync(string token)
    {
        try
        {
            var secret = FirstNonEmpty(
                _configuration["JWT__SECRET"],
                _configuration["JWT_SECRET"],
                _configuration["Jwt:Secret"],
                Environment.GetEnvironmentVariable("JWT__SECRET"));
            if (string.IsNullOrWhiteSpace(secret))
            {
                _logger.LogError("JWT__SECRET não definido; não é possível validar o access token");
                return Task.FromResult<UserContext?>(null);
            }

            var issuer = FirstNonEmpty(
                _configuration["JWT__ISSUER"],
                _configuration["Jwt:Issuer"],
                "Simcag.IdentityService");
            var audience = FirstNonEmpty(
                _configuration["JWT__AUDIENCE"],
                _configuration["Jwt:Audience"],
                "Simcag.Clients");

            var tokenHandler = new JwtSecurityTokenHandler();
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
            var validation = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ValidateIssuer = true,
                ValidIssuer = issuer,
                ValidateAudience = true,
                ValidAudience = audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };

            var principal = tokenHandler.ValidateToken(token, validation, out var secToken);
            if (secToken is not JwtSecurityToken jwt)
                return Task.FromResult<UserContext?>(null);

            var userId = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var name = principal.FindFirst("name")?.Value
                ?? principal.FindFirst(ClaimTypes.Name)?.Value
                ?? string.Empty;
            var email = principal.FindFirst(ClaimTypes.Email)?.Value
                ?? principal.FindFirst(JwtRegisteredClaimNames.Email)?.Value
                ?? string.Empty;
            var displayName = !string.IsNullOrEmpty(name) ? name : email;
            var roleString = principal.FindFirst(ClaimTypes.Role)?.Value
                ?? principal.FindAll("role").Select(c => c.Value).FirstOrDefault();
            var tenantId = principal.FindFirst(SimcagClaims.TenantId)?.Value ?? string.Empty;
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(roleString))
            {
                _logger.LogWarning("Token sem sub ou role");
                return Task.FromResult<UserContext?>(null);
            }

            var role = ParseRole(roleString);
            var permissions = new List<string>();
            var accessToken = new AccessToken(
                token,
                userId,
                displayName,
                role,
                permissions,
                jwt.ValidTo
            );

            return Task.FromResult<UserContext?>(
                new UserContext(
                    userId,
                    displayName,
                    tenantId,
                    role,
                    permissions,
                    accessToken
                ));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating token");
            return Task.FromResult<UserContext?>(null);
        }
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v))
                return v;
        }
        return null;
    }

    public Task<bool> HasPermissionAsync(UserContext userContext, string resource, string action) =>
        Task.FromResult(_access.IsAllowed(userContext, resource, action));

    private Simcag.Gateway.Domain.ValueObjects.Role ParseRole(string role)
    {
        return role.ToUpperInvariant() switch
        {
            "ADMIN" => Simcag.Gateway.Domain.ValueObjects.Role.ADMIN,
            "SINDICO" => Simcag.Gateway.Domain.ValueObjects.Role.SINDICO,
            "CONSELHO" => Simcag.Gateway.Domain.ValueObjects.Role.CONSELHO,
            "MORADOR" => Simcag.Gateway.Domain.ValueObjects.Role.MORADOR,
            _ => throw new InvalidOperationException($"Role desconhecida: {role}")
        };
    }

}
