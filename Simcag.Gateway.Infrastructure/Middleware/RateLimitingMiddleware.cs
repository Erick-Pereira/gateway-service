using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace Simcag.Gateway.Infrastructure.Middleware;

/// <summary>
/// Limite por janela de 1 minuto (Redis). Deve correr <b>depois</b> de <see cref="AuthenticationMiddleware"/>
/// para que pedidos autenticados usem o <c>sub</c> do utilizador em vez do IP partilhado (localhost/Docker/proxy).
/// <list type="bullet">
/// <item><description><c>RATE_LIMIT_REQUESTS</c> — omitido = 400/min; <c>0</c> = desativado.</description></item>
/// </list>
/// </summary>
public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IDistributedCache _cache;
    private readonly ILogger<RateLimitingMiddleware> _logger;
    private readonly int _maxRequestsPerMinute;

    public RateLimitingMiddleware(
        RequestDelegate next,
        IDistributedCache cache,
        ILogger<RateLimitingMiddleware> logger)
    {
        _next = next;
        _cache = cache;
        _logger = logger;
        _maxRequestsPerMinute = 400;
        var raw = Environment.GetEnvironmentVariable("RATE_LIMIT_REQUESTS");
        if (int.TryParse(raw, out var m) && m >= 0)
            _maxRequestsPerMinute = m;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (ShouldBypassRateLimit(context.Request.Path))
        {
            await _next(context);
            return;
        }

        if (_maxRequestsPerMinute == 0)
        {
            await _next(context);
            return;
        }

        var clientId = GetClientIdentifier(context);
        var rateLimitKey = $"rate_limit:{clientId}";

        var current = await _cache.GetStringAsync(rateLimitKey);
        var count = current != null ? int.Parse(current) : 0;

        if (count >= _maxRequestsPerMinute)
        {
            _logger.LogWarning("Rate limit exceeded for client {ClientId}", clientId);
            context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
            await context.Response.WriteAsync("Rate limit exceeded");
            return;
        }

        count++;
        var options = new DistributedCacheEntryOptions()
            .SetAbsoluteExpiration(TimeSpan.FromMinutes(1));

        await _cache.SetStringAsync(rateLimitKey, count.ToString(), options);

        await _next(context);
    }

    private static bool ShouldBypassRateLimit(PathString path)
    {
        var p = path.Value ?? string.Empty;
        if (p.Length == 0)
            return false;

        return p.StartsWith("/health", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("/metrics", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("/info", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("/openapi", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetClientIdentifier(HttpContext context)
    {
        if (context.User?.Identity?.IsAuthenticated == true)
        {
            return context.User.FindFirst("sub")?.Value
                ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? context.User.Identity?.Name
                ?? "authenticated:unknown";
        }

        var forwarded = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            var first = forwarded.Split(',')[0].Trim();
            if (first.Length > 0)
                return $"ip:{first}";
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
