using System.Net;
using Microsoft.AspNetCore.Http;

namespace Simcag.Gateway.Infrastructure.RateLimit;

/// <summary>
/// Middleware de Circuit Breaker para chamadas inter-serviços via YARP.
/// Protege o gateway contra falhas em cascata quando serviços downstream estão indisponíveis.
/// </summary>
public sealed class CircuitBreakerMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<CircuitBreakerMiddleware> _logger;
    private readonly CircuitBreakerOptions _options;

    public CircuitBreakerMiddleware(
        RequestDelegate next,
        ILogger<CircuitBreakerMiddleware> logger,
        CircuitBreakerOptions options)
    {
        _next = next;
        _logger = logger;
        _options = options;
    }

    /// <summary>
    /// Intercepta chamadas HTTP e aplica circuit breaker baseado no destino.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        var shouldBreakCircuit = ShouldBreakCircuit(path);

        if (shouldBreakCircuit)
        {
            await ApplyCircuitBreakerAsync(context);
        }
        else
        {
            await _next(context);
        }
    }

    /// <summary>
    /// Verifica se o caminho deve ser protegido por circuit breaker.
    /// </summary>
    private bool ShouldBreakCircuit(string path)
    {
        // Excluir URLs de health check e documentação
        if (_options.ExcludeUrls.Any(exclude => path.StartsWith(exclude, StringComparison.OrdinalIgnoreCase)))
            return false;

        // Apenas proteger chamadas para serviços downstream (não endpoints do gateway)
        var isDownstreamCall = path.StartsWith("/api/") && !path.Contains("/gateway");
        return isDownstreamCall;
    }

    /// <summary>
    /// Aplica circuit breaker à chamada HTTP.
    /// </summary>
    private async Task ApplyCircuitBreakerAsync(HttpContext context)
    {
        var downstreamService = ExtractDownstreamService(context.Request);
        var policyName = $"{_options.CircuitPrefix}{downstreamService}";

        try
        {
            // Executar a chamada normalmente (circuit breaker placeholder)
            await _next(context);
            
            // Em produção: registrar sucesso no circuit breaker
            // _logger.LogDebug("Circuit breaker success for {PolicyName}", policyName);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            // 503 do downstream - abrir circuito se necessário
            _logger.LogWarning(ex, "Service unavailable for {DownstreamService}. Circuit breaker may trip.", downstreamService);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in circuit breaker for {DownstreamService}", downstreamService);
            throw;
        }
    }

    /// <summary>
    /// Extrai o nome do serviço downstream da URL.
    /// </summary>
    private static string ExtractDownstreamService(HttpRequest request)
    {
        var uri = new Uri($"{request.Scheme}://{request.Host.Value}{request.PathBase}{request.Path}");
        var host = uri.Host;

        // Mapear hosts para nomes de serviço
        return host.ToLowerInvariant() switch
        {
            "identity-service" => "identity-service",
            "ingestion-service" => "ingestion-service",
            "processing-service" => "processing-service",
            "alert-service" => "alert-service",
            "notification-service" => "notification-service",
            "price-analysis-service" => "price-analysis-service",
            "market-data-service" => "market-data-service",
            "ai-service" => "ai-service",
            _ => host.Split(':')[0] // Extrair nome do host
        };
    }

    /// <summary>
    /// Retorna resposta de circuito aberto.
    /// </summary>
    private async Task ReturnCircuitOpenResponseAsync(HttpContext context, string downstreamService)
    {
        context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
        context.Response.Headers.Add("Retry-After", _options.ResetTimeoutSeconds.ToString());
        context.Response.Headers.Add("X-Simcag-Circuit-Breaker", "OPEN");
        context.Response.Headers.Add("X-Simcag-Downstream-Service", downstreamService);

        var response = new
        {
            error = "Service Unavailable",
            message = $"Serviço {downstreamService} indisponível. Tente novamente em {_options.ResetTimeoutSeconds} segundos.",
            retryAfter = _options.ResetTimeoutSeconds,
            circuitBreaker = "OPEN"
        };

        await context.Response.WriteAsJsonAsync(response);
    }
}
