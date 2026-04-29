namespace Simcag.Gateway.Application.Interfaces;

/// <summary>URLs base dos microserviços (sem barra no final) — alinhado ao YarpConfig.</summary>
public interface IGatewayServiceAddresses
{
    string Ingestion { get; }
    string Processing { get; }
    string Alert { get; }
    string Notification { get; }
    string Identity { get; }
    string GetByServiceName(string serviceName);
}
