namespace Simcag.Gateway.Infrastructure.RateLimit;

/// <summary>
/// Configurações específicas para Dead Letter Queues (DLQ) do gateway-service.
/// </summary>
public sealed class GatewayDlqOptions
{
    /// <summary>
    /// Habilita ou desabilita o envio de mensagens falhas para DLQ.
    /// </summary>
    public bool EnableDeadLetterQueue { get; set; } = true;

    /// <summary>
    /// Nome do exchange RabbitMQ para roteamento das DLQs.
    /// </summary>
    public string ExchangeName { get; set; } = "gateway.error.exchange";

    /// <summary>
    /// Prefixo para nomes de filas de DLQ (ex: gateway-proxy.dlq).
    /// </summary>
    public string DlqQueuePrefix { get; set; } = "gateway-";

    /// <summary>
    /// TTL da mensagem na DLQ em milissegundos (padrão: 24 horas).
    /// </summary>
    public int DlqMessageTtlMilliseconds { get; set; } = 86400000;

    /// <summary>
    /// Tipos de erros que devem ser roteados para DLQ.
    /// </summary>
    public IEnumerable<string> ErrorTypesToRoute { get; set; } = new[]
    {
        "TooManyRequests",           // Rate limit exceeded (429)
        "ServiceUnavailable",         // Downstream service unavailable (503)
        "GatewayTimeout",             // Timeout em chamadas downstream
        "ValidationError",            // Validação de requisição falhou
    };

    /// <summary>
    /// Se true, mensagens na DLQ são notificadas via webhook.
    /// </summary>
    public bool NotifyOnDlqMessage { get; set; } = false;

    /// <summary>
    /// URL do webhook para notificações de mensagens na DLQ.
    /// </summary>
    public string DlqWebhookUrl { get; set; } = string.Empty;
}
