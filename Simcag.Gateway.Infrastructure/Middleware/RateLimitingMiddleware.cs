using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using System.Net;

namespace Simcag.Gateway.Infrastructure.Middleware;

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
        _maxRequestsPerMinute = int.TryParse(Environment.GetEnvironmentVariable("RATE_LIMIT_REQUESTS"), out var m) && m > 0
            ? m
            : 100;
    }

    public async Task InvokeAsync(HttpContext context)
    {
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

    private string GetClientIdentifier(HttpContext context)
    {
        if (context.User?.Identity?.IsAuthenticated == true)
        {
            return context.User.FindFirst("sub")?.Value ?? context.User.Identity.Name ?? "anonymous";
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
