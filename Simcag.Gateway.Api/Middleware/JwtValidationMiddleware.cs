using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace Simcag.Gateway.Api.Middleware;

public class JwtValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<JwtValidationMiddleware> _logger;

    public JwtValidationMiddleware(RequestDelegate next, ILogger<JwtValidationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip JWT validation for auth endpoints and health checks
        if (context.Request.Path.StartsWithSegments("/api/auth") ||
            context.Request.Path.StartsWithSegments("/health") ||
            context.Request.Method == "OPTIONS")
        {
            await _next(context);
            return;
        }

        // Check if Authorization header exists
        if (!context.Request.Headers.ContainsKey("Authorization"))
        {
            _logger.LogWarning("Missing Authorization header for protected endpoint: {Path}", context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new
            {
                success = false,
                error = "Authorization header is required",
                code = "MissingAuthorization"
            });
            return;
        }

        // Let the JWT Bearer middleware handle the validation
        // If validation fails, it will set context.User to null and return 401
        await _next(context);

        // Log successful authentication
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var userId = context.User.FindFirst("sub")?.Value ??
                        context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            _logger.LogInformation("Authenticated request from user {UserId} to {Path}",
                userId, context.Request.Path);
        }
    }
}

// Extension method to add the middleware
public static class JwtValidationMiddlewareExtensions
{
    public static IApplicationBuilder UseJwtValidation(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<JwtValidationMiddleware>();
    }
}