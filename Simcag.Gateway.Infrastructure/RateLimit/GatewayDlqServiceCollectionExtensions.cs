using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Simcag.Gateway.Infrastructure.RateLimit;

/// <summary>
/// Extensões para configuração das Dead Letter Queues (DLQ) no gateway-service.
/// </summary>
public static class GatewayDlqServiceCollectionExtensions
{
    /// <summary>
    /// Configura as Dead Letter Queues (DLQ) para o gateway-service.
    /// </summary>
    public static IServiceCollection AddGatewayDlq(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = new GatewayDlqOptions();

        // Carregar configurações do appsettings.json
        configuration.GetSection("GatewayDlq").Bind(options);

        services.AddSingleton<GatewayDlqOptions>(options);

        return services;
    }

    /// <summary>
    /// Registra o middleware de DLQ no pipeline.
    /// Deve ser chamado após o YARP proxy para capturar erros downstream.
    /// </summary>
    public static IApplicationBuilder UseGatewayDlq(this IApplicationBuilder app)
    {
        app.UseMiddleware<GatewayDlqMiddleware>();
        return app;
    }
}
