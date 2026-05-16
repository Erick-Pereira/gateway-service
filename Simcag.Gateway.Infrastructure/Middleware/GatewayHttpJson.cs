using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace Simcag.Gateway.Infrastructure.Middleware;

/// <summary>Respostas JSON consistentes para o SPA (evita corpo só texto em 401/403).</summary>
internal static class GatewayHttpJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static async Task WriteErrorAsync(
        HttpContext context,
        int statusCode,
        string message,
        string code,
        string? resource = null,
        string? action = null)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";

        object payload = resource != null && action != null
            ? new { success = false, message, code, resource, action }
            : new { success = false, message, code };

        await context.Response.WriteAsync(JsonSerializer.Serialize(payload, Options));
    }
}
