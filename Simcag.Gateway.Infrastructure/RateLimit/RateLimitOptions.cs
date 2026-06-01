namespace Simcag.Gateway.Infrastructure.RateLimit;

/// <summary>
/// Opções de Rate Limiting configuráveis por rota.
/// </summary>
public class RateLimitOptions
{
    /// <summary>Limite padrão: 60 requisições por minuto.</summary>
    public const int DefaultRequestsPerMinute = 60;

    /// <summary>TTL em segundos para contagem de requisições (1 minuto).</summary>
    public const int DefaultTtlSeconds = 60;

    /// <summary>Prefixo de chave Redis para multi-tenancy.</summary>
    public const string RedisKeyPrefix = "ratelimit:";

    /// <summary>Limite padrão por rota (requests per minute).</summary>
    public int RequestsPerMinute { get; set; } = DefaultRequestsPerMinute;

    /// <summary>TTL em segundos para contagem de requisições.</summary>
    public int TtlSeconds { get; set; } = DefaultTtlSeconds;

    /// <summary>Se true, retorna 429 mesmo quando abaixo do limite (modo teste).</summary>
    public bool ForceRateLimit { get; set; } = false;
}
