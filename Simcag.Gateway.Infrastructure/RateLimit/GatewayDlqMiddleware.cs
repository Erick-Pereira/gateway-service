using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Simcag.Gateway.Infrastructure.RateLimit;

/// <summary>
/// Middleware que captura erros do YARP proxy e os registra para Dead Letter Queues (DLQ).
/// Em produção, esta implementação deve ser integrada com RabbitMQ via Simcag.Shared.Messaging.RabbitMQ.
/// </summary>
public sealed class GatewayDlqMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GatewayDlqMiddleware> _logger;
    private readonly GatewayDlqOptions _options;

    public GatewayDlqMiddleware(
        RequestDelegate next,
        ILogger<GatewayDlqMiddleware> logger,
        GatewayDlqOptions options)
    {
        _next = next;
        _logger = logger;
        _options = options;
    }

    /// <summary>
    /// Intercepta respostas com status de erro e registra para DLQ.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        var originalStatusCode = context.Response.StatusCode;

        // Apenas interceptar erros de gateway (não downstream 5xx)
        if (originalStatusCode >= 500 && ShouldRouteToDlq(originalStatusCode))
        {
            await RouteToDlqAsync(context, originalStatusCode);
            return;
        }

        await _next(context);
    }

    /// <summary>
    /// Verifica se o status deve ser roteado para DLQ.
    /// </summary>
    private bool ShouldRouteToDlq(int statusCode)
    {
        var errorType = GetErrorTypeFromStatusCode(statusCode);
        return _options.ErrorTypesToRoute.Contains(errorType, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Mapeia status code para tipo de erro.
    /// </summary>
    private static string GetErrorTypeFromStatusCode(int statusCode) => statusCode switch
    {
        429 => "TooManyRequests",
        503 => "ServiceUnavailable",
        _ when statusCode >= 500 && statusCode < 600 => "GatewayTimeout",
        _ => null
    };

    /// <summary>
    /// Registra o erro para a DLQ.
    /// </summary>
    private async Task RouteToDlqAsync(HttpContext context, int statusCode)
    {
        var dlqQueueName = GetDlqQueueName(statusCode);
        var errorMessage = BuildErrorMessage(context, statusCode);

        _logger.LogError(
            "Gateway error routed to DLQ: Queue={QueueName}, StatusCode={StatusCode}, ErrorType={ErrorType}",
            dlqQueueName, statusCode, GetErrorTypeFromStatusCode(statusCode));

        // Em produção: publicar mensagem na DLQ via RabbitMQ
        // var envelope = new MessageEnvelope { ... };
        // await PublishToDlqAsync(dlqQueueName, json);

        // Para desenvolvimento: apenas logar (RabbitMQ não configurado)
        if (_options.EnableDeadLetterQueue)
        {
            try
            {
                var envelope = new
                {
                    timestamp = DateTime.UtcNow.ToString("o"),
                    path = context.Request.Path.Value,
                    method = context.Request.Method,
                    protocol = context.Request.Protocol,
                    statusCode = statusCode,
                    clientIp = context.Connection.RemoteIpAddress?.ToString(),
                    userAgent = context.Request.Headers["User-Agent"].FirstOrDefault(),
                    errorType = GetErrorTypeFromStatusCode(statusCode),
                    errorMessage = errorMessage,
                };

                var json = JsonSerializer.Serialize(envelope);
                await SaveToDlqStorageAsync(dlqQueueName, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha ao salvar erro na DLQ: {ErrorMessage}", errorMessage);
            }
        }
    }

    /// <summary>
    /// Gera o nome da fila de DLQ baseado no status code.
    /// </summary>
    private string GetDlqQueueName(int statusCode) => $"{_options.DlqQueuePrefix}{statusCode}.dlq";

    /// <summary>
    /// Constrói mensagem de erro formatada.
    /// </summary>
    private static string BuildErrorMessage(HttpContext context, int statusCode)
    {
        var errorDetails = new
        {
            timestamp = DateTime.UtcNow.ToString("o"),
            path = context.Request.Path.Value,
            method = context.Request.Method,
            protocol = context.Request.Protocol,
            statusCode = statusCode,
            clientIp = context.Connection.RemoteIpAddress?.ToString(),
            userAgent = context.Request.Headers["User-Agent"].FirstOrDefault(),
        };

        return JsonSerializer.Serialize(errorDetails);
    }

    /// <summary>
    /// Salva mensagem na DLQ (implementação stub para desenvolvimento).
    /// Em produção, substituir por integração com RabbitMQ.
    /// </summary>
    private static async Task SaveToDlqStorageAsync(string queueName, string message)
    {
        // Implementação stub - em produção usar RabbitMQ ou similar
        // Exemplo com Redis:
        /*
        var redis = ConnectionMultiplexer.Connect("redis:6379");
        var db = redis.GetDatabase();
        await db.ListRightPushAsync(queueName, message);
        */
    }
}
