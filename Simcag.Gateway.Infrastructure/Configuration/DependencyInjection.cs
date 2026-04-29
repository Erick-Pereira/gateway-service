using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Extensions.Http;
using Simcag.Gateway.Application.Interfaces;
using Simcag.Gateway.Application.Services;
using Simcag.Gateway.Infrastructure.Middleware;
using Simcag.Gateway.Infrastructure.Proxy;
using Yarp.ReverseProxy.Configuration;

namespace Simcag.Gateway.Infrastructure.Configuration;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        // Sem REDIS_CONNECTION (ou "memory" / "inmemory"): IDistributedCache em memória (dev local sem StackExchange).
        // Em produção ou com Redis: defina REDIS_CONNECTION, ex. "redis:6379" ou "localhost:6379".
        var redisConnection = GetRedisCacheConnection();
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

    public static string? GetRedisCacheConnection() =>
        FirstNonEmpty(
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
