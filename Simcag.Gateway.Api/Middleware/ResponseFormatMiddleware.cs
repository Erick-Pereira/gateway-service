using Microsoft.AspNetCore.Http;
using System.Text.Json;

namespace Simcag.Gateway.Api.Middleware;

/// <summary>
/// Envolve respostas JSON em { success, data, errors, metadata } para rotas de API,
/// exceto rotas de diagnóstico (/health, /info, /swagger, /openapi).
/// </summary>
public class ResponseFormatMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ResponseFormatMiddleware> _logger;

    private static readonly HashSet<string> BypassPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "/health", "/info", "/swagger", "/openapi"
    };

    // Rotas de proxy de documentação OpenAPI dos downstream (ex: /api/identity-docs/swagger/...)
    private static bool IsDocProxyPath(PathString path) =>
        path.Value?.Contains("/swagger/", StringComparison.OrdinalIgnoreCase) == true
        || path.Value?.EndsWith("/swagger.json", StringComparison.OrdinalIgnoreCase) == true;

    public ResponseFormatMiddleware(RequestDelegate next, ILogger<ResponseFormatMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Passa sem interceptar para rotas de diagnóstico, Swagger local e proxy de docs downstream.
        if (ShouldBypass(context.Request.Path) || IsDocProxyPath(context.Request.Path))
        {
            await _next(context);
            return;
        }

        // Captura o body para poder ler e reescrever.
        var originalBodyStream = context.Response.Body;
        using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        await _next(context);

        responseBody.Seek(0, SeekOrigin.Begin);
        var responseBodyText = await new StreamReader(responseBody).ReadToEndAsync();

        // Só encapsula respostas JSON que ainda não estejam no formato { "success": ... }.
        if (context.Response.ContentType?.Contains("application/json") == true
            && !string.IsNullOrEmpty(responseBodyText)
            && !responseBodyText.TrimStart().StartsWith("{\"success\"", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var formatted = new
                {
                    success  = context.Response.StatusCode is >= 200 and < 300,
                    data     = JsonSerializer.Deserialize<object>(responseBodyText,
                                   new JsonSerializerOptions { PropertyNameCaseInsensitive = true }),
                    errors   = Array.Empty<string>(),
                    metadata = new { Timestamp = DateTime.UtcNow }
                };

                // Serializa para um stream separado para evitar truncagem do MemoryStream original.
                using var formattedStream = new MemoryStream();
                await JsonSerializer.SerializeAsync(formattedStream, formatted,
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

                // Atualiza Content-Length e copia para o stream externo (pode ser o do ResponseCachingMiddleware).
                context.Response.ContentLength = formattedStream.Length;
                formattedStream.Seek(0, SeekOrigin.Begin);
                await formattedStream.CopyToAsync(originalBodyStream);
                return;
            }
            catch (JsonException)
            {
                // fallthrough: copia original abaixo
            }
        }

        // Resposta não-JSON, já formatada ou com erro de desserialização → copia original.
        responseBody.Seek(0, SeekOrigin.Begin);
        await responseBody.CopyToAsync(originalBodyStream);
    }

    private static bool ShouldBypass(PathString path)
    {
        foreach (var prefix in BypassPrefixes)
            if (path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}
