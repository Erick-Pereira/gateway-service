using Microsoft.AspNetCore.Http;
using System.Diagnostics;

namespace Simcag.Gateway.Infrastructure.Middleware;

/// <summary>
/// Garante que toda requisição tenha um <c>X-Correlation-Id</c>, gerando um GUID
/// quando ausente. Propaga para serviços downstream e devolve no header de resposta.
/// </summary>
public sealed class CorrelationIdMiddleware : IMiddleware
{
    public const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (!context.Request.Headers.TryGetValue(HeaderName, out var existing) || string.IsNullOrWhiteSpace(existing))
        {
            var generated = Guid.NewGuid().ToString();
            context.Request.Headers[HeaderName] = generated;
        }

        var correlationId = context.Request.Headers[HeaderName].ToString();
        Activity.Current?.SetTag("simcag.correlation_id", correlationId);
        context.Response.OnStarting(() =>
        {
            if (!context.Response.Headers.ContainsKey(HeaderName))
                context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        await next(context);
    }
}
