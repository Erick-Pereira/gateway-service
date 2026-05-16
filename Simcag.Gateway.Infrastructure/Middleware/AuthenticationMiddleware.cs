using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Simcag.Gateway.Application.Interfaces;
using Simcag.Gateway.Domain.Entities;
using Simcag.Shared.Security;

namespace Simcag.Gateway.Infrastructure.Middleware;

public class AuthenticationMiddleware : IMiddleware
{
    private readonly IAuthService _authService;
    private readonly ILogger<AuthenticationMiddleware> _logger;
    private readonly IOptionsMonitor<GatewayTrustOptions> _trustOptions;
    private readonly List<string> _publicEndpoints = new()
    {
        "/api/auth/login",
        "/api/auth/register",
        "/api/auth/refresh",
        "/api/auth/logout",
        "/api/auth/validate",
        "/api/auth/setup",
        "/api/condominios/lookup",
        "/health",
        "/info",
        "/metrics"
    };

    public AuthenticationMiddleware(
        IAuthService authService,
        ILogger<AuthenticationMiddleware> logger,
        IOptionsMonitor<GatewayTrustOptions> trustOptions)
    {
        _authService = authService;
        _logger = logger;
        _trustOptions = trustOptions;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var path = context.Request.Path.ToString();

        if (IsPublicPath(path))
        {
            // Rotas públicas: browsers costumam enviar Bearer antigo (localStorage). O JwtBearer
            // (UseAuthentication) valida qualquer Authorization presente e falha com token expirado
            // antes do proxy — e o Identity pode devolver 500/401 indevidos no lookup anónimo.
            // Exceção: /api/auth/validate precisa do header para o controller inspecionar o JWT.
            if (!path.StartsWith("/api/auth/validate", StringComparison.OrdinalIgnoreCase))
                context.Request.Headers.Remove("Authorization");

            await next(context);
            return;
        }

        var token = context.Request.Headers["Authorization"].FirstOrDefault()?.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(token))
        {
            await GatewayHttpJson.WriteErrorAsync(
                context,
                StatusCodes.Status401Unauthorized,
                "Token não fornecido.",
                "NO_TOKEN");
            return;
        }

        try
        {
            var userContext = await _authService.ValidateTokenAsync(token);

            if (userContext == null)
            {
                await GatewayHttpJson.WriteErrorAsync(
                    context,
                    StatusCodes.Status401Unauthorized,
                    "Token inválido ou expirado.",
                    "INVALID_TOKEN");
                return;
            }

            var claims = new List<Claim>
            {
                new Claim("sub", userContext.UserId),
                new Claim("role", userContext.Role.ToString()),
                new Claim(ClaimTypes.Role, userContext.Role.ToString()),
                new Claim("name", userContext.UserName),
                new Claim(SimcagClaims.TenantId, userContext.TenantId ?? string.Empty)
            };

            var identity = new ClaimsIdentity(claims, "Bearer");
            var principal = new ClaimsPrincipal(identity);
            context.User = principal;

            context.Items["UserContext"] = userContext;

            // Header propagado para serviços downstream identificarem o tenant sem revalidar JWT.
            if (!string.IsNullOrWhiteSpace(userContext.TenantId))
            {
                context.Request.Headers[GatewayForwardedAuthHeaders.TenantId] = userContext.TenantId;
            }
            context.Request.Headers[GatewayForwardedAuthHeaders.UserId] = userContext.UserId;
            context.Request.Headers[GatewayForwardedAuthHeaders.UserRole] = userContext.Role.ToString();
            if (!string.IsNullOrWhiteSpace(userContext.UserName))
                context.Request.Headers[GatewayForwardedAuthHeaders.UserName] = userContext.UserName;

            AppendGatewayProof(context, userContext);

            await next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro na autenticação");
            await GatewayHttpJson.WriteErrorAsync(
                context,
                StatusCodes.Status401Unauthorized,
                "Erro na autenticação.",
                "AUTH_ERROR");
        }
    }

    private void AppendGatewayProof(HttpContext context, UserContext userContext)
    {
        var opt = _trustOptions.CurrentValue;
        if (string.IsNullOrWhiteSpace(opt.DownstreamHmacSecret))
            return;

        var unix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var tenant = userContext.TenantId ?? string.Empty;
        var role = userContext.Role.ToString();
        var sig = GatewayDownstreamHmac.ComputeSignature(opt.DownstreamHmacSecret!, unix, userContext.UserId, tenant, role);
        context.Request.Headers[GatewayDownstreamProofHeaders.TimestampUnix] = unix.ToString(CultureInfo.InvariantCulture);
        context.Request.Headers[GatewayDownstreamProofHeaders.Signature] = sig;
    }

    private bool IsPublicPath(string path)
    {
        if (_publicEndpoints.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            return true;
        if (path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase))
            return true;
        if (path.StartsWith("/openapi", StringComparison.OrdinalIgnoreCase))
            return true;
        // Documentos OpenAPI agregados (cada serviço expõe /swagger e o gateway proxia em /api/<servico>-docs)
        if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) && path.Contains("-docs/", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }
}
