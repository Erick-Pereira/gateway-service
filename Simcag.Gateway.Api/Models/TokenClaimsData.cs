namespace Simcag.Gateway.Api;

using Simcag.Shared.Security;

/// <summary>
/// Dados extraídos do JWT para validação RBAC.
/// </summary>
public readonly record struct TokenClaimsData
{
    public string RequestPath { get; init; }
    public string? Role { get; init; }
    public string? SubjectId { get; init; }
    public string? PermissionClaim { get; init; }
    public string? Profile { get; init; }
    
    // SoD constraints
    public bool SodCanExecuteAudit { get; init; }   // false = não pode executar auditoria operativa
    public bool CanApproveOwnPurchase { get; init; } // false = não aprova própria compra (SoD)
    
    // Dashboard access by profile
    public bool DashboardFullRead { get; init; }

    /// <summary>
    /// Verifica se os claims são válidos para o endpoint em questão.
    /// </summary>
    public bool IsValid
    {
        get
        {
            if (Role == SimcagRoles.Morador)
            {
                var perm = PermissionClaim ?? string.Empty;
                return !perm.Contains("compras:approve", StringComparison.OrdinalIgnoreCase) &&
                       !perm.Contains("auditoria:upload", StringComparison.OrdinalIgnoreCase) &&
                       !SodCanExecuteAudit;
            }

            // Admin válido quando SoD impede aprovar própria compra (claim sod:can_approve_own_purchase=false).
            if (Role == SimcagRoles.Admin)
                return !CanApproveOwnPurchase;

            if (Role is SimcagRoles.Sindico or SimcagRoles.Conselho)
                return !SodCanExecuteAudit;

            return true;
        }
    }

    /// <summary>
    /// Motivo do rejection se invalid.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// Exception para reject 403 com erro específico (não expondo payloads maliciosos).
    /// </summary>
    public Exception? Exception { get; init; }
}