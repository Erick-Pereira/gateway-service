using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Simcag.Shared.Security;

namespace Simcag.Gateway.Api.Middleware;

/// <summary>
/// Middleware centralizado de autorização RBAC no gateway.
/// Intercepta HTTP requests, valida JWT claims e aplica regras SoD por endpoint.
///
/// **Como funciona:**
/// 1. Verifica se usuário está autenticado (JWT válido)
/// 2. Extrai claims do token (role, permissions, tenant_id)
/// 3. Aplica políticas de Segregation of Duties (SoD):
///    - Admin não pode aprovar próprias compras
///    - Uploads de documentos para Administradora e Síndico
///    - Auditoria exclusiva para Admin perfil
///
/// **Endpoints públicos:** /health, /swagger, /api/auth/*, /api/condominios/lookup
/// </summary>
public class RbacAuthorizationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RbacAuthorizationMiddleware> _logger;

    public RbacAuthorizationMiddleware(
        RequestDelegate next,
        ILogger<RbacAuthorizationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            if (context.User.Identity?.IsAuthenticated != true)
            {
                if (!IsPublicRoute(context.Request.Path))
                {
                    await RejectUnauthorizedAsync(context, "Autenticação JWT expirada ou inválida");
                    return;
                }
            }

            var claims = GetTokenClaims(context);

            if (!claims.IsValid)
            {
                _logger.LogWarning(
                    "RBAC Validation failed for path '{RequestPath}' | Role '{Role}'",
                    context.Request.Path,
                    claims.Role);

                if (claims.Exception?.GetType().Name == "AccessDeniedException")
                {
                    await RejectForbiddenAsync(
                        context,
                        new Dictionary<string, string>
                        {
                            ["error"] = "RBAC_PermissionDenied",
                            ["message"] = claims.Reason ?? "Permissão negada."
                        });
                    return;
                }
            }

            await ValidateSegregationOfDutiesAsync(context, claims);

            _logger.LogDebug(
                "RBAC Passed for path '{RequestPath}' | Role '{Role}'",
                context.Request.Path,
                claims.Role);

            await _next.Invoke(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RBAC Middleware erro ao validar request");

            if (!context.Request.Path.StartsWithSegments("/swagger"))
            {
                await RejectUnauthorizedAsync(
                    context,
                    "Erro de validação RBAC — por favor, contate Admin");
            }
        }
    }

    private static TokenClaimsData GetTokenClaims(HttpContext context) => new()
    {
        RequestPath = context.Request.Path.Value ?? string.Empty,
        Role = ExtractRoleFromClaims(context.User),
        SubjectId = ExtractSubjectFromClaims(context.User),
        PermissionClaim = context.User.FindFirst(SimcagClaims.Permission)?.Value,
        Profile = context.User.FindFirst("profile")?.Value,
        SodCanExecuteAudit = HasClaimValue(context.User, SimcagClaims.SodCanExecuteAudit),
        CanApproveOwnPurchase = HasClaimValue(context.User, SimcagClaims.SodCanApproveOwnPurchase),
        DashboardFullRead = HasClaimValue(context.User, "permissions.dashboard.read:full"),
    };

    private static string? ExtractRoleFromClaims(ClaimsPrincipal user) =>
        user.FindFirst(SimcagClaims.Role)?.Value;

    private static bool HasClaimValue(ClaimsPrincipal user, string claimType) =>
        string.Equals(user.FindFirst(claimType)?.Value, "true", StringComparison.OrdinalIgnoreCase);

    private static string? ExtractSubjectFromClaims(ClaimsPrincipal user) =>
        user.FindFirst("sub")?.Value;

    private async Task ValidateSegregationOfDutiesAsync(HttpContext context, TokenClaimsData claims)
    {
        if (IsOwnPurchase(context.Request.Path, claims.SubjectId))
        {
            if (!claims.CanApproveOwnPurchase && claims.Role == SimcagRoles.Admin)
            {
                await RejectForbiddenAsync(
                    context,
                    new Dictionary<string, string>
                    {
                        ["error"] = "SegregationOfDuties",
                        ["message"] = "Administradora não pode aprovar própria compra — regra de negócio."
                    });
                return;
            }
        }

        if (IsAuditExecutionEndpoint(context.Request.Path) &&
            (!claims.SodCanExecuteAudit || !string.Equals(claims.Profile, "Admin", StringComparison.OrdinalIgnoreCase)))
        {
            await RejectForbiddenAsync(
                context,
                new Dictionary<string, string>
                {
                    ["error"] = "SegregationOfDuties",
                    ["message"] =
                        $"Execução de auditoria exclusiva da Administradora. Perfil '{claims.Profile}' não autorizado."
                });
            return;
        }

        if (IsDocumentUploadEndpoint(context))
        {
            if (string.Equals(claims.Role, SimcagRoles.Admin, StringComparison.OrdinalIgnoreCase)
                || string.Equals(claims.Role, SimcagRoles.Sindico, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await RejectForbiddenAsync(
                context,
                new Dictionary<string, string>
                {
                    ["error"] = "RBAC_PermissionDenied",
                    ["message"] =
                        $"Upload de documentos permitido apenas para Administradora ou Síndico. Perfil '{claims.Profile ?? claims.Role}' não autorizado."
                });
            return;
        }
    }

    private static bool IsDocumentUploadEndpoint(HttpContext context) =>
        context.Request.Path.StartsWithSegments("/api/ingestion/upload", StringComparison.OrdinalIgnoreCase)
        || context.Request.Path.StartsWithSegments("/api/auditoria/upload", StringComparison.OrdinalIgnoreCase);

    private static bool IsAuditExecutionEndpoint(PathString path) =>
        path.StartsWithSegments("/api/auditoria/executar") ||
        path.Value?.Contains("/initiate-audit", StringComparison.OrdinalIgnoreCase) == true ||
        path.Value?.Contains("/complete-audit", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsOwnPurchase(PathString path, string? userId) =>
        path.Value?.Contains("/purchases/", StringComparison.OrdinalIgnoreCase) == true &&
        path.Value.Contains("/approve", StringComparison.OrdinalIgnoreCase) &&
        ExtractPurchaseOwnerIdFromPath(path.Value) == userId;

    private static string? ExtractPurchaseOwnerIdFromPath(string path) =>
        path.Split('/')
            .Where(s => !string.IsNullOrEmpty(s))
            .LastOrDefault();

    private async Task RejectUnauthorizedAsync(HttpContext context, string reason)
    {
        if (!context.Request.Path.StartsWithSegments("/swagger"))
        {
            _logger.LogWarning(
                "RBAC Unauthorized for path '{Path}' | Reason: {Reason}",
                context.Request.Path,
                reason);
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = "Unauthorized", message = reason });
    }

    private async Task RejectForbiddenAsync(HttpContext context, Dictionary<string, string> errorData)
    {
        if (!context.Request.Path.StartsWithSegments("/swagger") &&
            !context.Request.Path.StartsWithSegments("/health"))
        {
            _logger.LogWarning(
                "RBAC Forbidden - Path: '{Path}' | Error Type: '{ErrorType}' | Message: '{ErrorMessage}'",
                context.Request.Path,
                errorData["error"],
                errorData["message"]);
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(errorData);
    }

    private static bool IsPublicRoute(PathString path) =>
        path.StartsWithSegments("/health") ||
        path.StartsWithSegments("/swagger") ||
        path.StartsWithSegments("/api/auth") ||
        path.StartsWithSegments("/api/condominios/lookup");
}
