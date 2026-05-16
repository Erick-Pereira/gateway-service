namespace Simcag.Gateway.Application.Authorization;

/// <summary>
/// Nomes canónicos de recurso para <see cref="IGatewayAccessEvaluator"/> e para permissões <c>resource:action</c>.
/// Uma única fonte de verdade evita drift entre middleware de rotas e regras de acesso.
/// </summary>
public static class GatewayAccessResources
{
    public const string Ingestion = "ingestion";
    public const string Admin = "admin";
    public const string Alert = "alert";
    public const string Notification = "notification";
    public const string Report = "report";
}

/// <summary>Ações lógicas avaliadas no edge (não confundir com verbos HTTP).</summary>
public static class GatewayAccessActions
{
    public const string Read = "read";
    public const string Write = "write";
    public const string Manage = "manage";
    public const string Wildcard = "*";
}
