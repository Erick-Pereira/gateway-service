using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Simcag.Shared.Security;

namespace Simcag.Gateway.Infrastructure.Middleware;

/// <summary>
/// Middleware de cache estratégico para dashboard e market data.
/// Otimizado para endpoints de leitura frequente com dados relativamente estáticos.
/// </summary>
public sealed class ResponseCachingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IDistributedCache _cache;
    private readonly ILogger<ResponseCachingMiddleware> _logger;
    private readonly TimeSpan _dashboardAbsoluteExpiration;
    private readonly TimeSpan _dashboardSlidingExpiration;
    private readonly TimeSpan _marketDataAbsoluteExpiration;
    private readonly TimeSpan _marketDataSlidingExpiration;

    public ResponseCachingMiddleware(
        RequestDelegate next,
        IDistributedCache cache,
        ILogger<ResponseCachingMiddleware> logger)
    {
        _next = next;
        _cache = cache;
        _logger = logger;

        // Cache estratégico para dashboard (dados mais estáticos)
        _dashboardAbsoluteExpiration = ParseDuration("GATEWAY_DASHBOARD_CACHE_ABSOLUTE_SECONDS", TimeSpan.FromMinutes(5));
        _dashboardSlidingExpiration = ParseDuration("GATEWAY_DASHBOARD_CACHE_SLIDING_SECONDS", TimeSpan.FromMinutes(2));

        // Cache estratégico para market data (dados mais dinâmicos)
        _marketDataAbsoluteExpiration = ParseDuration("GATEWAY_MARKET_DATA_CACHE_ABSOLUTE_SECONDS", TimeSpan.FromMinutes(1));
        _marketDataSlidingExpiration = ParseDuration("GATEWAY_MARKET_DATA_CACHE_SLIDING_SECONDS", TimeSpan.FromSeconds(30));
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

    /// <summary>
    /// Endpoints que devem sempre bypassar o cache (dados em tempo real ou writes).
    /// </summary>
    private static readonly HashSet<string> AlwaysBypassCache = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/expenses",
        "/api/ingestion",
        "/api/compliance",
        "/api/payments",
        "/api/alerts",
        "/api/AlertRules",
        "/api/notifications",
        "/api/audit-logs",
    };

    /// <summary>
    /// Endpoints de dashboard que devem ser cacheados com TTL mais longo.
    /// </summary>
    private static readonly HashSet<string> DashboardEndpoints = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/dashboard",
        "/api/dashboard/summary",
        "/api/dashboard/kpis",
        "/api/dashboard/overview",
    };

    /// <summary>
    /// Endpoints de market data que devem ser cacheados com TTL mais curto.
    /// </summary>
    private static readonly HashSet<string> MarketDataEndpoints = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/market-data",
        "/api/market-data/prices",
        "/api/market-data/products",
        "/api/market-data/benchmarks",
    };

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // Bypass para métodos não GET e rotas proibidas
        if (!context.Request.Method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            || NoCachePrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase))
            || IsDocProxyPath(path))
        {
            await _next(context);
            return;
        }

        // Sempre bypass para endpoints de dados em tempo real
        if (AlwaysBypassCache.Contains(path) || AlwaysBypassCache.Any(p => path.StartsWith(p)))
        {
            ApplyNoStoreHeaders(context.Response);
            await _next(context);
            return;
        }

        var cacheKey = GenerateCacheKey(context.Request);
        var isDashboardEndpoint = DashboardEndpoints.Contains(path) || DashboardEndpoints.Any(p => path.StartsWith(p));
        var isMarketDataEndpoint = MarketDataEndpoints.Contains(path) || MarketDataEndpoints.Any(p => path.StartsWith(p));
        var requestBypassesCache = RequestBypassesCache(context.Request);

        if (requestBypassesCache)
        {
            ApplyNoStoreHeaders(context.Response);
            await _next(context);
            return;
        }

        // Dashboard: cache mais longo (dados relativamente estáticos)
        if (isDashboardEndpoint)
        {
            var cachedResponse = await _cache.GetStringAsync(cacheKey);

            if (cachedResponse != null)
            {
                _logger.LogDebug("Dashboard cache hit: {CacheKey}", cacheKey);
                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json";
                context.Response.Headers.CacheControl = "private, max-age=300"; // 5 minutos
                context.Response.Headers["X-Simcag-Cache"] = "HIT";
                context.Response.Headers["X-Simcag-Cache-Type"] = "dashboard";
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
                    .SetAbsoluteExpiration(_dashboardAbsoluteExpiration)
                    .SetSlidingExpiration(_dashboardSlidingExpiration);

                await _cache.SetStringAsync(cacheKey, responseBodyText, cacheOptions);

                _logger.LogInformation("Dashboard response cached: {CacheKey}", cacheKey);
                context.Response.Headers["X-Simcag-Cache"] = "MISS";
                context.Response.Headers["X-Simcag-Cache-Type"] = "dashboard";

                responseBody.Seek(0, SeekOrigin.Begin);
                await responseBody.CopyToAsync(originalBodyStream);
            }
            else
            {
                responseBody.Seek(0, SeekOrigin.Begin);
                await responseBody.CopyToAsync(originalBodyStream);
            }
            return;
        }

        // Market Data: cache mais curto (dados dinâmicos)
        if (isMarketDataEndpoint)
        {
            var cachedResponse = await _cache.GetStringAsync(cacheKey);

            if (cachedResponse != null)
            {
                _logger.LogDebug("Market data cache hit: {CacheKey}", cacheKey);
                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json";
                context.Response.Headers.CacheControl = "private, max-age=30"; // 30 segundos
                context.Response.Headers["X-Simcag-Cache"] = "HIT";
                context.Response.Headers["X-Simcag-Cache-Type"] = "market-data";
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
                    .SetAbsoluteExpiration(_marketDataAbsoluteExpiration)
                    .SetSlidingExpiration(_marketDataSlidingExpiration);

                await _cache.SetStringAsync(cacheKey, responseBodyText, cacheOptions);

                _logger.LogInformation("Market data response cached: {CacheKey}", cacheKey);
                context.Response.Headers["X-Simcag-Cache"] = "MISS";
                context.Response.Headers["X-Simcag-Cache-Type"] = "market-data";

                responseBody.Seek(0, SeekOrigin.Begin);
                await responseBody.CopyToAsync(originalBodyStream);
            }
            else
            {
                responseBody.Seek(0, SeekOrigin.Begin);
                await responseBody.CopyToAsync(originalBodyStream);
            }
            return;
        }

        // Outros endpoints: sem cache (bypass)
        ApplyNoStoreHeaders(context.Response);
        await _next(context);
    }

    private string GenerateCacheKey(HttpRequest request)
    {
        var path = request.Path.ToString();
        var query = request.QueryString.ToString();
        var tenantId = request.Headers[GatewayForwardedAuthHeaders.TenantId].FirstOrDefault() ?? "no-tenant";
        return $"gw:cache:{tenantId}:{path}{query}";
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
        return !cacheControl.Contains("no-store", StringComparison.OrdinalIgnoreCase);
    }

    private static void ApplyNoStoreHeaders(HttpResponse response)
    {
        response.Headers.CacheControl = "no-store, no-cache, max-age=0";
        response.Headers.Pragma = "no-cache";
        response.Headers.Expires = "0";
    }

    private static TimeSpan ParseDuration(string envKey, TimeSpan fallback)
    {
        var raw = Environment.GetEnvironmentVariable(envKey);
        return int.TryParse(raw, out var seconds) && seconds > 0 && seconds <= 3600
            ? TimeSpan.FromSeconds(seconds)
            : fallback;
    }
}
