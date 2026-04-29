using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace Simcag.Gateway.Infrastructure.Middleware;

public class ResponseCachingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IDistributedCache _cache;
    private readonly ILogger<ResponseCachingMiddleware> _logger;

    public ResponseCachingMiddleware(
        RequestDelegate next,
        IDistributedCache cache,
        ILogger<ResponseCachingMiddleware> logger)
    {
        _next = next;
        _cache = cache;
        _logger = logger;
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

        var cacheKey = GenerateCacheKey(context.Request);

        var cachedResponse = await _cache.GetStringAsync(cacheKey);

        if (cachedResponse != null)
        {
            _logger.LogInformation("Cache hit: {CacheKey}", cacheKey);
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(cachedResponse);
            return;
        }

        var originalBodyStream = context.Response.Body;
        using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        await _next(context);

        if (context.Response.StatusCode == 200)
        {
            responseBody.Seek(0, SeekOrigin.Begin);
            var responseBodyText = await new StreamReader(responseBody).ReadToEndAsync();

            var cacheOptions = new DistributedCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromMinutes(5))
                .SetSlidingExpiration(TimeSpan.FromMinutes(2));

            await _cache.SetStringAsync(cacheKey, responseBodyText, cacheOptions);

            _logger.LogInformation("Response cached: {CacheKey}", cacheKey);

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
        var userId = request.Headers["X-User-Id"].FirstOrDefault() ?? "anonymous";
        return $"gw:cache:{userId}:{path}{query}";
    }
}
