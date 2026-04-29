using Microsoft.AspNetCore.Http;
using Simcag.Gateway.Domain.Entities;
using Simcag.Gateway.Application.Interfaces;

namespace Simcag.Gateway.Infrastructure.Middleware;

public class AuthorizationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AuthorizationMiddleware> _logger;
    private readonly IGatewayAccessEvaluator _access;

    public AuthorizationMiddleware(
        RequestDelegate next,
        ILogger<AuthorizationMiddleware> logger,
        IGatewayAccessEvaluator access)
    {
        _next = next;
        _logger = logger;
        _access = access;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.ToString();
        var method = context.Request.Method;

        var (resource, action) = ExtractResourceAndAction(path, method);

        if (string.IsNullOrEmpty(resource) || string.IsNullOrEmpty(action))
        {
            await _next(context);
            return;
        }

        var userContext = context.Items["UserContext"] as UserContext;

        if (userContext == null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Usuário não autenticado");
            return;
        }

        if (!_access.IsAllowed(userContext, resource, action))
        {
            _logger.LogWarning("Acesso negado para usuário {UserId} ao recurso {Resource}:{Action}", userContext.UserId, resource, action);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync($"Acesso negado a {resource}:{action}");
            return;
        }

        await _next(context);
    }

    private (string? resource, string? action) ExtractResourceAndAction(string path, string method)
    {
        var mappings = new Dictionary<string, (string, string)>
        {
            ["/api/ingestion/upload"] = ("ingestion", "write"),
            ["/api/ingestion/"] = ("ingestion", "read"),
            ["/api/alerts"] = ("alert", "read"),
            ["/api/alerts/"] = ("alert", "manage"),
            ["/api/notifications"] = ("notification", "read"),
            ["/api/notifications/"] = ("notification", "manage"),
            ["/api/audit/report"] = ("report", "read"),
            ["/api/audit/"] = ("report", "read"),
            ["/api/admin/"] = ("admin", "*")
        };

        foreach (var mapping in mappings)
        {
            if (path.StartsWith(mapping.Key, StringComparison.OrdinalIgnoreCase))
            {
                return mapping.Value;
            }
        }

        return (null, null);
    }
}
