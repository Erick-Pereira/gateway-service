using Simcag.Gateway.Application.Interfaces;
using Simcag.Gateway.Infrastructure.Proxy;

namespace Simcag.Gateway.Infrastructure.Configuration;

public sealed class GatewayServiceAddresses : IGatewayServiceAddresses
{
    public string Ingestion => YarpConfig.ClusterAddress(
        "http://ingestion-service:8081",
        "GATEWAY_INGESTION_ADDRESS",
        "SERVICES__INGESTION__URL",
        "INGESTION_SERVICE_URL").TrimEnd('/');

    public string Processing => YarpConfig.ClusterAddress(
        "http://processing-service:8082",
        "GATEWAY_PROCESSING_ADDRESS",
        "SERVICES__PROCESSING__URL",
        "PROCESSING_SERVICE_URL").TrimEnd('/');

    public string Alert => YarpConfig.ClusterAddress(
        "http://alert-service:8083",
        "GATEWAY_ALERT_ADDRESS",
        "SERVICES__ALERT__URL",
        "ALERT_SERVICE_URL").TrimEnd('/');

    public string Notification => YarpConfig.ClusterAddress(
        "http://notification-service:8084",
        "GATEWAY_NOTIFICATION_ADDRESS",
        "SERVICES__NOTIFICATION__URL",
        "NOTIFICATION_SERVICE_URL").TrimEnd('/');

    public string Identity => YarpConfig.ClusterAddress(
        "http://identity-service:8080",
        "GATEWAY_IDENTITY_ADDRESS",
        "SERVICES__IDENTITY__URL",
        "IDENTITY_SERVICE_URL").TrimEnd('/');

    public string GetByServiceName(string serviceName) =>
        serviceName.ToLowerInvariant() switch
        {
            "ingestion" => Ingestion,
            "processing" => Processing,
            "alert" => Alert,
            "notification" => Notification,
            "identity" => Identity,
            "admin" => Identity,
            _ => throw new ArgumentException($"Serviço desconhecido: {serviceName}", nameof(serviceName))
        };
}
