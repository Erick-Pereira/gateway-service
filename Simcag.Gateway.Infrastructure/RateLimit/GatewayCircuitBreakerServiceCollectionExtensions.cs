using Polly;
using Polly.CircuitBreaker;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http;
using System.Threading.Tasks;

namespace Simcag.Gateway.Infrastructure.RateLimit;

/// <summary>
/// Extensão para registrar políticas de Circuit Breaker com Polly.
/// </summary>
public static class GatewayCircuitBreakerServiceCollectionExtensions
{
    /// <summary>
    /// Adiciona suporte a Circuit Breaker via Polly ao serviço.
    /// </summary>
    public static IServiceCollection AddGatewayCircuitBreaker(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Ler configurações de variáveis de ambiente
        var failureThreshold = GetEnvInt(configuration, "GATEWAY_CIRCUIT_BREAKER_FAILURE_THRESHOLD", 5);
        var resetTimeoutSeconds = GetEnvInt(configuration, "GATEWAY_CIRCUIT_BREAKER_RESET_TIMEOUT_SECONDS", 30);

        // Registrar HttpClientHandler com Circuit Breaker para cada serviço downstream
        var downstreamServices = new[]
        {
            ("identity-service", "identity-service"),
            ("ingestion-service", "ingestion-service"),
            ("processing-service", "processing-service"),
            ("alert-service", "alert-service"),
            ("notification-service", "notification-service"),
            ("price-analysis-service", "price-analysis-service"),
            ("market-data-service", "market-data-service"),
            ("ai-service", "ai-service"),
        };

        foreach (var (host, serviceName) in downstreamServices)
        {
            var policyName = $"CircuitBreaker.{serviceName}";
            
            services.AddHttpClient($"downstream-{serviceName}", client =>
            {
                client.BaseAddress = new Uri($"http://{host}/");
            })
            .AddPolicyHandler(GetCircuitBreakerPolicy(serviceName, failureThreshold, resetTimeoutSeconds));
        }

        return services;
    }

    /// <summary>
    /// Obtém valor inteiro de variável de ambiente com fallback.
    /// </summary>
    private static int GetEnvInt(IConfiguration configuration, string key, int defaultValue)
    {
        var envValue = Environment.GetEnvironmentVariable(key);
        if (int.TryParse(envValue, out var value))
            return value;

        var settingValue = configuration[key];
        if (int.TryParse(settingValue, out var settingValueInt))
            return settingValueInt;

        return defaultValue;
    }

    /// <summary>
    /// Retorna a política de Circuit Breaker para o serviço especificado.
    /// </summary>
    private static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy(
        string serviceName,
        int failureThreshold,
        int resetTimeoutSeconds)
    {
        var resetTimeout = TimeSpan.FromSeconds(resetTimeoutSeconds);

        return Policy.HandleResult<HttpResponseMessage>(r => r.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
            .Or<HttpRequestException>()
            .Or<TaskCanceledException>()
            .CircuitBreakerAsync(
                failureThreshold,
                resetTimeout);
    }
}
