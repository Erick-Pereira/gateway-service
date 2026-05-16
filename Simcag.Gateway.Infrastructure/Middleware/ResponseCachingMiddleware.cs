using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Simcag.Shared.Security;

namespace Simcag.Gateway.Infrastructure.Middleware;

public class ResponseCachingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IDistributedCache _cache;
    private readonly ILogger<ResponseCachingMiddleware> _logger;
    private readonly string[] _cacheablePrefixes;
    private readonly TimeSpan _absoluteExpiration;
    private readonly TimeSpan _slidingExpiration;

    public ResponseCachingMiddleware(
        RequestDelegate next,
        IDistributedCache cache,
        ILogger<ResponseCachingMiddleware> logger)
    {
        _next = next;
        _cache = cache;
        _logger = logger;
        _cacheablePrefixes = ParsePrefixes(Environment.GetEnvironmentVariable("GATEWAY_RESPONSE_CACHE_PREFIXES"));
        _absoluteExpiration = ParseDuration("GATEWAY_RESPONSE_CACHE_ABSOLUTE_SECONDS", TimeSpan.FromSeconds(30));
        _slidingExpiration = ParseDuration("GATEWAY_RESPONSE_CACHE_SLIDING_SECONDS", TimeSpan.FromSeconds(10));
    }

    // Rotas que nunca devem ser cacheadas (diagnóstico, Swagger, autenticação).
    private static readonly HashSet<string> NoCachePrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "/health", "/info", "/swagger", "/openapi", "/api/auth"
    };

    // Proxy de documentação OpenAPI dos serviços downstream nunca deve ser cacheado.
    private static bool IsDocProxyPath(string path) =>
        path.Contains("/swagger/", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith("/swagger.json", StringComparison.OrdinalIgnoreCase);

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        if (!context.Request.Method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            || NoCachePrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase))
            || IsDocProxyPath(path))
        {
            await _next(context);
            return;
        }

        if (!IsCacheablePath(path) || RequestBypassesCache(context.Request))
        {
            ApplyNoStoreHeaders(context.Response);
            await _next(context);
            return;
        }

        var cacheKey = GenerateCacheKey(context.Request);

        var cachedResponse = await _cache.GetStringAsync(cacheKey);

        if (cachedResponse != null)
        {
            _logger.LogDebug("Gateway response cache hit: {CacheKey}", cacheKey);
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            context.Response.Headers.CacheControl = "private, max-age=30";
            context.Response.Headers["X-Simcag-Cache"] = "HIT";
            await context.Response.WriteAsync(cachedResponse);
            return;
        }

        var originalBodyStream = context.Response.Body;
        using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        await _next(context);

        if (CanStoreResponse(context.Response))
        {
            responseBody.Seek(0, SeekOrigin.Begin);
            var responseBodyText = await new StreamReader(responseBody).ReadToEndAsync();

            var cacheOptions = new DistributedCacheEntryOptions()
                .SetAbsoluteExpiration(_absoluteExpiration)
                .SetSlidingExpiration(_slidingExpiration);

            await _cache.SetStringAsync(cacheKey, responseBodyText, cacheOptions);

            _logger.LogDebug("Gateway response cached: {CacheKey}", cacheKey);
            context.Response.Headers["X-Simcag-Cache"] = "MISS";

            responseBody.Seek(0, SeekOrigin.Begin);
            await responseBody.CopyToAsync(originalBodyStream);
        }
        else
        {
            responseBody.Seek(0, SeekOrigin.Begin);
            await responseBody.CopyToAsync(originalBodyStream);
        }
    }

    private string GenerateCacheKey(HttpRequest request)
    {
        var path = request.Path.ToString();
        var query = request.QueryString.ToString();
        var userId = request.Headers[GatewayForwardedAuthHeaders.UserId].FirstOrDefault() ?? "anonymous";
        var tenantId = request.Headers[GatewayForwardedAuthHeaders.TenantId].FirstOrDefault() ?? "no-tenant";
        var role = request.Headers[GatewayForwardedAuthHeaders.UserRole].FirstOrDefault() ?? "no-role";
        return $"gw:cache:{tenantId}:{userId}:{role}:{path}{query}";
    }

    private bool IsCacheablePath(string path)
    {
        if (_cacheablePrefixes.Length == 0)
            return false;

        return _cacheablePrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static bool RequestBypassesCache(HttpRequest request)
    {
        var cacheControl = request.Headers.CacheControl.ToString();
        var pragma = request.Headers.Pragma.ToString();

        return cacheControl.Contains("no-cache", StringComparison.OrdinalIgnoreCase)
               || cacheControl.Contains("no-store", StringComparison.OrdinalIgnoreCase)
               || pragma.Contains("no-cache", StringComparison.OrdinalIgnoreCase);
    }

    private static bool CanStoreResponse(HttpResponse response)
    {
        if (response.StatusCode != StatusCodes.Status200OK)
            return false;

        if (response.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) != true)
            return false;

        var cacheControl = response.Headers.CacheControl.ToString();
        return !cacheControl.Contains("no-store", StringComparison.OrdinalIgnoreCase)
               && !cacheControl.Contains("private", StringComparison.OrdinalIgnoreCase);
    }

    private static void ApplyNoStoreHeaders(HttpResponse response)
    {
        response.Headers.CacheControl = "no-store, no-cache, max-age=0";
        response.Headers.Pragma = "no-cache";
        response.Headers.Expires = "0";
    }

    private static string[] ParsePrefixes(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        return raw
            .Split(';', ',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(prefix => prefix.StartsWith('/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static TimeSpan ParseDuration(string envKey, TimeSpan fallback)
    {
        var raw = Environment.GetEnvironmentVariable(envKey);
        return int.TryParse(raw, out var seconds) && seconds > 0 && seconds <= 3600
            ? TimeSpan.FromSeconds(seconds)
            : fallback;
    }
}
