using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Simcag.Gateway.Infrastructure.Middleware;

public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var request = context.Request;
        var correlationId = context.TraceIdentifier;

        var logEntry = new
        {
            CorrelationId = correlationId,
            Timestamp = DateTime.UtcNow,
            Method = request.Method,
            Path = request.Path,
            QueryString = request.QueryString.ToString(),
            ClientIp = context.Connection.RemoteIpAddress?.ToString(),
            UserAgent = request.Headers["User-Agent"].ToString(),
            UserId = context.User?.Identity?.IsAuthenticated == true ? context.User.FindFirst("sub")?.Value : null
        };

        _logger.LogInformation("Request received: {@LogEntry}", logEntry);

        try
        {
            await _next(context);

            stopwatch.Stop();
            var completed = new
            {
                logEntry.CorrelationId,
                logEntry.Timestamp,
                logEntry.Method,
                logEntry.Path,
                logEntry.QueryString,
                logEntry.ClientIp,
                logEntry.UserAgent,
                logEntry.UserId,
                StatusCode = context.Response.StatusCode,
                DurationMs = stopwatch.ElapsedMilliseconds
            };

            _logger.LogInformation("Request completed: {@LogEntry}", completed);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Request failed: {CorrelationId} after {DurationMs}ms", correlationId, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}
