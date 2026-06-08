namespace Simcag.Gateway.Application.Authorization;

/// <summary>
/// Mapeia prefixos de URL para par (recurso, ação) usado por <see cref="IGatewayAccessEvaluator"/>.
/// Ordem importa: prefixos mais específicos primeiro (ex.: upload antes de /api/ingestion/).
/// </summary>
public static class GatewayAuthorizationPathCatalog
{
    /// <summary>
    /// Prefixos em ordem de precedência (primeira correspondência vence).
    /// </summary>
    private static readonly (string Prefix, string Resource, string Action)[] OrderedMappings =
    {
        ("/api/ingestion/upload", GatewayAccessResources.Ingestion, GatewayAccessActions.Write),
        ("/api/ingestion/", GatewayAccessResources.Ingestion, GatewayAccessActions.Read),
        ("/api/alerts", GatewayAccessResources.Alert, GatewayAccessActions.Read),
        ("/api/alerts/", GatewayAccessResources.Alert, GatewayAccessActions.Manage),
        ("/api/notifications/operational/", GatewayAccessResources.Notification, GatewayAccessActions.Manage),
        ("/api/notifications/deliveries", GatewayAccessResources.Notification, GatewayAccessActions.Manage),
        ("/api/notifications/send", GatewayAccessResources.Notification, GatewayAccessActions.Manage),
        ("/api/notifications/email", GatewayAccessResources.Notification, GatewayAccessActions.Manage),
        ("/api/notifications/sms", GatewayAccessResources.Notification, GatewayAccessActions.Manage),
        ("/api/notifications/governance", GatewayAccessResources.Notification, GatewayAccessActions.Read),
        ("/api/notifications/templates", GatewayAccessResources.Notification, GatewayAccessActions.Read),
        ("/api/audit/report", GatewayAccessResources.Report, GatewayAccessActions.Read),
        ("/api/audit/", GatewayAccessResources.Report, GatewayAccessActions.Read),
        ("/api/admin/", GatewayAccessResources.Admin, GatewayAccessActions.Wildcard),
    };

    /// <summary>
    /// Resolve recurso e ação para autorização no gateway, ou devolve vazio se o path não for governado aqui.
    /// </summary>
    /// <param name="path">Path da requisição (ex.: <see cref="Microsoft.AspNetCore.Http.HttpRequest.Path"/>).</param>
    /// <param name="httpMethod">Usado para distinguir leitura vs gravação em preferências de notificação.</param>
    public static bool TryResolveResourceAction(string path, string httpMethod, out string resource, out string action)
    {
        var p = path ?? string.Empty;
        var method = httpMethod ?? "GET";

        if (p.StartsWith("/api/notifications/preferences", StringComparison.OrdinalIgnoreCase))
        {
            resource = GatewayAccessResources.Notification;
            action = IsWriteMethod(method) ? GatewayAccessActions.Write : GatewayAccessActions.Read;
            return true;
        }

        foreach (var (prefix, res, act) in OrderedMappings)
        {
            if (p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                resource = res;
                action = act;
                return true;
            }
        }

        if (p.StartsWith("/api/notifications", StringComparison.OrdinalIgnoreCase))
        {
            resource = GatewayAccessResources.Notification;
            action = GatewayAccessActions.Read;
            return true;
        }

        resource = string.Empty;
        action = string.Empty;
        return false;
    }

    private static bool IsWriteMethod(string method) =>
        string.Equals(method, "PUT", StringComparison.OrdinalIgnoreCase)
        || string.Equals(method, "PATCH", StringComparison.OrdinalIgnoreCase)
        || string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase);
}
