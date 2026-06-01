using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Simcag.Gateway.Api.Controllers;

/// <summary>
/// Controller para health checks e status do serviço.
/// - GET /health      → inclui downstreams (pode demorar vários segundos)
/// - GET /health/live → só o processo gateway (+ Redis se configurado); para Docker HEALTHCHECK
/// - GET /info        → metadados do serviço
/// 
/// ⚠️ Tratamento de exceções: Usa IExceptionHandler global (RFC 7807) para evitar vazamento de detalhes.
/// </summary>
[ApiController]
[Route("")]
public class StatusController : ControllerBase
{
    private readonly HealthCheckService _healthCheckService;
    private readonly ILogger<StatusController> _logger;

    public StatusController(HealthCheckService healthCheckService, ILogger<StatusController> logger)
    {
        _healthCheckService = healthCheckService;
        _logger = logger;
    }

    /// <summary>
    /// Health check do gateway.
    /// Retorna 200 quando saudável ou degraded (downstreams offline).
    /// Retorna 503 apenas quando o próprio gateway está unhealthy.
    /// 
    /// ⚠️ Erros são tratados pelo IExceptionHandler global (RFC 7807) - sem vazamento de detalhes.
    /// </summary>
    [HttpGet("health")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Health()
    {
        var report = await _healthCheckService.CheckHealthAsync();

        var entries = report.Entries.Select(e => new
        {
            name    = e.Key,
            status  = e.Value.Status.ToString(),
            message = e.Value.Description,
            tags    = e.Value.Tags
        });

        var body = new
        {
            status    = report.Status.ToString().ToLowerInvariant(),
            timestamp = DateTime.UtcNow,
            checks    = entries
        };

        // Degraded (algum downstream offline) ainda é operacional → 200
        // Unhealthy (problema interno do gateway) → 503
        return report.Status == HealthStatus.Unhealthy
            ? StatusCode(StatusCodes.Status503ServiceUnavailable, body)
            : Ok(body);
    }

    /// <summary>
    /// Metadados do serviço.
    /// </summary>
    [HttpGet("info")]
    public IActionResult Info()
    {
        return Ok(new
        {
            service     = "gateway-service",
            version     = "1.0.0",
            environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development",
            timestamp   = DateTime.UtcNow
        });
    }
}
