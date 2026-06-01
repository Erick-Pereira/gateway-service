namespace Simcag.Gateway.Infrastructure.RateLimit;

/// <summary>
/// Configurações para Circuit Breaker via Polly.
/// </summary>
public sealed class CircuitBreakerOptions
{
    /// <summary>
    /// Número mínimo de falhas consecutivas antes de abrir o circuito.
    /// </summary>
    public int FailureThreshold { get; set; } = 5;

    /// <summary>
    /// Tempo em segundos para tentar reestabelecer o circuito (half-open state).
    /// </summary>
    public int ResetTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Tempo em segundos para considerar uma chamada como bem-sucedida antes de fechar o circuito.
    /// </summary>
    public int SuccessThreshold { get; set; } = 3;

    /// <summary>
    /// Prefixo para nomes de circuitos (ex: identity-service, market-data-service).
    /// </summary>
    public string CircuitPrefix { get; set; } = "gateway-circuit:";

    /// <summary>
    /// Se true, falhas no circuito são logadas como erros críticos.
    /// </summary>
    public bool LogCircuitOpenAsError { get; set; } = true;

    /// <summary>
    /// URLs que devem ser excluídas do circuit breaker (health checks, etc.).
    /// </summary>
    public IEnumerable<string> ExcludeUrls { get; set; } = new[]
    {
        "/health",
        "/info",
        "/swagger",
        "/openapi",
    };
}
