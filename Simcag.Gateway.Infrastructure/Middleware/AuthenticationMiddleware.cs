using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Simcag.Gateway.Domain.Entities;
using Simcag.Gateway.Application.Interfaces;

namespace Simcag.Gateway.Infrastructure.Middleware;

public class AuthenticationMiddleware : IMiddleware
{
    private readonly IAuthService _authService;
    private readonly ILogger<AuthenticationMiddleware> _logger;
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
        ILogger<AuthenticationMiddleware> logger)
    {
        _authService = authService;
        _logger = logger;
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
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Token não fornecido");
            return;
        }

        try
        {
            var userContext = await _authService.ValidateTokenAsync(token);

            if (userContext == null)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Token inválido");
                return;
            }

            var claims = new List<Claim>
            {
                new Claim("sub", userContext.UserId),
                new Claim("role", userContext.Role.ToString()),
                new Claim(ClaimTypes.Role, userContext.Role.ToString()),
                new Claim("name", userContext.UserName),
                new Claim("tenant_id", userContext.TenantId ?? string.Empty)
            };

            var identity = new ClaimsIdentity(claims, "Bearer");
            var principal = new ClaimsPrincipal(identity);
            context.User = principal;

            context.Items["UserContext"] = userContext;

            // Header propagado para serviços downstream identificarem o tenant sem revalidar JWT.
            if (!string.IsNullOrWhiteSpace(userContext.TenantId))
            {
                context.Request.Headers["X-Tenant-Id"] = userContext.TenantId;
            }
            context.Request.Headers["X-User-Id"] = userContext.UserId;
            context.Request.Headers["X-User-Role"] = userContext.Role.ToString();

            await next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro na autenticação");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Erro na autenticação");
        }
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
