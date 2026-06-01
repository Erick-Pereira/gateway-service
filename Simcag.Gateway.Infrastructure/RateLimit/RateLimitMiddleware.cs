using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Simcag.Gateway.Infrastructure.RateLimit;

/// <summary>
/// Middleware de Rate Limiting usando Redis para contagem de requisições por rota.
/// Limite configurável por rota (default: 60 req/min).
/// Resposta 429 com header Retry-After quando excedido.
/// </summary>
public class RateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RateLimitMiddleware> _logger;
    private readonly ConnectionMultiplexer _redis;
    private readonly IDatabase _db;
    private readonly RateLimitOptions _options;

    public RateLimitMiddleware(
        RequestDelegate next,
        ILogger<RateLimitMiddleware> logger,
        IConnectionMultiplexer redis,
        IOptions<RateLimitOptions> options)
    {
        _next = next;
        _logger = logger;
        _redis = (ConnectionMultiplexer)redis;
        _db = _redis.GetDatabase();
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var key = BuildRateLimitKey(context);

        if (_options.ForceRateLimit)
        {
            _logger.LogWarning("Rate limit forcé ativado para rota: {Path}", context.Request.Path);
            await SendRateLimitedResponseAsync(context, key);
            return;
        }

        var now = DateTime.UtcNow;
        var windowStart = now.AddSeconds(-_options.TtlSeconds);

        // Limpar contagens antigas
        _db.KeyExpire(key, TimeSpan.FromSeconds(_options.TtlSeconds));
        
        // Verificar se excedeu limite
        var count = await _db.StringIncrementAsync($"{key}:count", 1);
        
        if (count > _options.RequestsPerMinute)
        {
            _logger.LogWarning(
                "Rate limit excedido para rota: {Path} - Contador: {Count}/Limit: {Limit}",
                context.Request.Path, count, _options.RequestsPerMinute);

            await SendRateLimitedResponseAsync(context, key);
            return;
        }

        // Registrar timestamp da última requisição
        await _db.StringSetAsync($"{key}:last", now.ToString("O"));

        await _next(context);
    }

    private string BuildRateLimitKey(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "/";
        var method = context.Request.Method;
        
        // Normalizar caminho para evitar problemas com query strings
        var normalizedPath = path.Split('?')[0];
        
        return $"{RateLimitOptions.RedisKeyPrefix}{method}:{normalizedPath}";
    }

    private async Task SendRateLimitedResponseAsync(HttpContext context, string key)
    {
        var retryAfter = TimeSpan.FromSeconds(_options.TtlSeconds);
        
        // Limpar contador atual para reiniciar janela
        await _db.KeyDeleteAsync(key);

        context.Response.StatusCode = 429;
        context.Response.Headers.Add("Retry-After", retryAfter.TotalSeconds.ToString());
        context.Response.Headers.Add("X-RateLimit-Limit", _options.RequestsPerMinute.ToString());
        context.Response.Headers.Add("X-RateLimit-Remaining", "0");

        var response = new
        {
            error = "Too Many Requests",
            message = $"Limite de requisições excedido. Tente novamente em {retryAfter.TotalSeconds:F0} segundos.",
            retryAfter = retryAfter.TotalSeconds,
            limit = _options.RequestsPerMinute
        };

        await context.Response.WriteAsJsonAsync(response);
        
        _logger.LogWarning(
            "Resposta 429 enviada para rota: {Path} - Retry-After: {RetryAfter}s",
            context.Request.Path, retryAfter.TotalSeconds);
    }
}
