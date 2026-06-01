using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Extensions.Http;
using Simcag.Gateway.Application.Interfaces;
using Simcag.Gateway.Application.Services;
using Simcag.Gateway.Infrastructure.Middleware;
using Simcag.Gateway.Infrastructure.Proxy;
using Simcag.Gateway.Infrastructure.RateLimit;
using Yarp.ReverseProxy.Configuration;

// Importar extensão de Circuit Breaker
using static Simcag.Gateway.Infrastructure.RateLimit.GatewayCircuitBreakerServiceCollectionExtensions;

namespace Simcag.Gateway.Infrastructure.Configuration;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Configurar Circuit Breaker antes de DLQs
        services.AddGatewayCircuitBreaker(configuration);

        // Configurar DLQs antes de Redis (DLQ precisa do RabbitMQ)
        services.AddGatewayDlq(configuration);

        // Sem REDIS_CONNECTION (ou "memory" / "inmemory"): IDistributedCache em memória (dev local sem StackExchange).
        // Em produção ou com Redis: defina REDIS_CONNECTION, ex. "redis:6379" ou "localhost:6379".
        var redisConnection = GetRedisCacheConnection(configuration);
        if (IsInProcessDistributedCache(redisConnection))
            services.AddDistributedMemoryCache();
        else
            services.AddStackExchangeRedisCache(options => { options.Configuration = redisConnection!; });

        // HttpClient Factory
        services.AddHttpClient();

        services.AddHttpClient("default")
            .AddPolicyHandler(HttpPolicyExtensions
                .HandleTransientHttpError()
                .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))));

        services.AddSingleton<IGatewayServiceAddresses, GatewayServiceAddresses>();
        services.AddSingleton<IGatewayAccessEvaluator, GatewayAccessEvaluator>();

        services.AddScoped<AuthenticationMiddleware>();
        services.AddScoped<CorrelationIdMiddleware>();

        // Services (apenas autenticação — todo o roteamento é feito pelo YARP)
        services.AddScoped<IAuthService, AuthService>();

        // YARP: rotas e clusters a partir de código; endereços dos destinos vêm de variáveis de ambiente (.env)
        services.AddReverseProxy()
            .LoadFromMemory(
                [..YarpConfig.GetRoutes()],
                [..YarpConfig.GetClusters()]);

        return services;
    }

    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        // Fallback para compatibilidade - chama com IConfiguration padrão
        var configuration = new ConfigurationBuilder()
            .SetBasePath(System.AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();

        return AddInfrastructure(services, configuration);
    }

    public static string? GetRedisCacheConnection(IConfiguration configuration = null) =>
        FirstNonEmpty(
            configuration?.GetValue<string>("REDIS__CONNECTION"),
            configuration?.GetValue<string>("REDIS_CONNECTION"),
            configuration?.GetValue<string>("ConnectionStrings__Redis"),
            Environment.GetEnvironmentVariable("REDIS__CONNECTION"),
            Environment.GetEnvironmentVariable("REDIS_CONNECTION"),
            Environment.GetEnvironmentVariable("ConnectionStrings__Redis"));

    public static bool IsInProcessDistributedCache(string? connection) =>
        string.IsNullOrWhiteSpace(connection)
        || connection.Equals("memory", StringComparison.OrdinalIgnoreCase)
        || connection.Equals("inmemory", StringComparison.OrdinalIgnoreCase);

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v))
                return v;
        }
        return null;
    }
}
