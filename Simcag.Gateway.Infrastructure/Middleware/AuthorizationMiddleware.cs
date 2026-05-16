using Microsoft.AspNetCore.Http;
using Simcag.Gateway.Application.Authorization;
using Simcag.Gateway.Application.Interfaces;
using Simcag.Gateway.Domain.Entities;

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

        if (!GatewayAuthorizationPathCatalog.TryResolveResourceAction(path, method, out var resource, out var action))
        {
            await _next(context);
            return;
        }

        var userContext = context.Items["UserContext"] as UserContext;

        if (userContext == null)
        {
            await GatewayHttpJson.WriteErrorAsync(
                context,
                StatusCodes.Status401Unauthorized,
                "Utilizador não autenticado.",
                "UNAUTHENTICATED");
            return;
        }

        if (!_access.IsAllowed(userContext, resource, action))
        {
            _logger.LogWarning("Acesso negado para usuário {UserId} ao recurso {Resource}:{Action}", userContext.UserId, resource, action);
            var message = GatewayForbiddenResponseMessages.For(resource, action);

            await GatewayHttpJson.WriteErrorAsync(
                context,
                StatusCodes.Status403Forbidden,
                message,
                "FORBIDDEN",
                resource,
                action);
            return;
        }

        await _next(context);
    }
}
