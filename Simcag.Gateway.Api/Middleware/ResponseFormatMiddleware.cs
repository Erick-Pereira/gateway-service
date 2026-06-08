using Microsoft.AspNetCore.Http;
using Simcag.Gateway.Infrastructure.Middleware;
using System.Text.Json;

namespace Simcag.Gateway.Api.Middleware;

/// <summary>
/// Envolve respostas JSON em { success, data, errors, metadata } para rotas de API,
/// exceto rotas de diagnóstico (/health, /info, /swagger, /openapi).
/// </summary>
public sealed class ResponseFormatMiddleware
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
        context.Response.OnStarting(static state =>
        {
            ((HttpContext)state!).AddSecurityHeaders();
            return Task.CompletedTask;
        }, context);

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

        try
        {
            await _next(context);

            responseBody.Seek(0, SeekOrigin.Begin);
            var responseBodyText = await new StreamReader(responseBody).ReadToEndAsync();

            // Só encapsula respostas JSON que ainda não estejam no formato { "success": ... }.
            if (context.Response.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true
                && !string.IsNullOrEmpty(responseBodyText)
                && !responseBodyText.TrimStart().StartsWith("{\"success\"", StringComparison.OrdinalIgnoreCase))
            {
                var correlationId = context.Request.Headers[CorrelationIdMiddleware.HeaderName].ToString();
                try
                {
                    object metadata = string.IsNullOrWhiteSpace(correlationId)
                        ? new { timestamp = DateTime.UtcNow }
                        : new { timestamp = DateTime.UtcNow, correlationId };

                    var formatted = new
                    {
                        success  = context.Response.StatusCode is >= 200 and < 300,
                        data     = JsonSerializer.Deserialize<object>(responseBodyText,
                                       new JsonSerializerOptions { PropertyNameCaseInsensitive = true }),
                        errors   = Array.Empty<string>(),
                        metadata
                    };

                    using var formattedStream = new MemoryStream();
                    await JsonSerializer.SerializeAsync(formattedStream, formatted,
                        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

                    context.Response.ContentLength = formattedStream.Length;
                    formattedStream.Seek(0, SeekOrigin.Begin);
                    context.Response.Body = originalBodyStream;
                    await formattedStream.CopyToAsync(originalBodyStream);
                    return;
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex,
                        "Gateway ResponseFormat: corpo não é JSON válido para encapsular; a devolver resposta original. Status={Status}",
                        context.Response.StatusCode);
                }
            }

            // Resposta não-JSON, já formatada ou com erro de desserialização → copia original.
            responseBody.Seek(0, SeekOrigin.Begin);
            context.Response.Body = originalBodyStream;
            await responseBody.CopyToAsync(originalBodyStream);
        }
        finally
        {
            context.Response.Body = originalBodyStream;
        }
    }

    private static bool ShouldBypass(PathString path)
    {
        foreach (var prefix in BypassPrefixes)
            if (path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}

/// <summary>Headers de segurança HTTP (extensão separada — exigência do compilador C#).</summary>
public static class GatewaySecurityHeadersExtensions
{
    public static void AddSecurityHeaders(this HttpContext context)
    {
        if (context.Response.HasStarted)
            return;

        var headers = context.Response.Headers;

        // Prevent clickjacking attacks
        headers["X-Frame-Options"] = "DENY";

        var cspDirectives = @"default-src 'self'; script-src 'self' 'unsafe-eval'; style-src 'self' 'unsafe-inline'; img-src 'self' data: https:; font-src 'self'; frame-ancestors 'none'; form-action 'self'; base-uri 'self'";
        headers["Content-Security-Policy"] = cspDirectives;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-XSS-Protection"] = "1; mode=block";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["Permissions-Policy"] = "geolocation=(), camera=(), microphone=()";

        if (!context.Request.Path.StartsWithSegments("/swagger"))
        {
            headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
            headers["Pragma"] = "no-cache";
            headers["Expires"] = "0";
        }

        if (!context.Request.Headers.ContainsKey("Accept"))
            headers["Vary"] = "Accept";
    }
}
