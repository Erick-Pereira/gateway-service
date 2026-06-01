using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Simcag.Gateway.Infrastructure.RateLimit;

namespace Simcag.Gateway.Infrastructure;

/// <summary>
/// Extensões para configuração do Rate Limiting no gateway-service.
/// </summary>
public static class RateLimitServiceCollectionExtensions
{
    /// <summary>
    /// Configura o middleware de Rate Limiting com limites por rota.
    /// </summary>
    public static IServiceCollection AddRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Adicionar singleton de Redis ConnectionMultiplexer
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var redis = configuration.GetConnectionString("Redis");
            if (string.IsNullOrEmpty(redis))
                throw new InvalidOperationException("Conexão Redis não configurada para Rate Limiting.");

            return ConnectionMultiplexer.Connect(redis);
        });

        // Configurar opções de rate limiting diretamente do IConfiguration
        services.Configure<RateLimitOptions>(configuration.GetSection("RateLimiting"));

        return services;
    }

    /// <summary>
    /// Registra o middleware de Rate Limiting no pipeline.
    /// </summary>
    public static IApplicationBuilder UseRateLimiting(this IApplicationBuilder app)
    {
        app.UseMiddleware<RateLimitMiddleware>();
        return app;
    }
}
