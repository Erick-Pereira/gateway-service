namespace Simcag.Gateway.Application.Authorization;

/// <summary>
/// Mensagens HTTP 403 para o edge — separadas do middleware para facilitar revisão de copy e testes.
/// </summary>
public static class GatewayForbiddenResponseMessages
{
    public const string IngestionWriteDenied =
        "Apenas o papel Síndico (ou administrador) pode enviar documentos para ingestão. O perfil Conselho tem acesso de leitura à auditoria, mas não pode carregar ficheiros.";

    public static string Default(string resource, string action) =>
        $"Acesso negado ao recurso solicitado ({resource}:{action}).";

    public static string For(string resource, string action)
    {
        if (string.Equals(resource, GatewayAccessResources.Ingestion, StringComparison.OrdinalIgnoreCase)
            && string.Equals(action, GatewayAccessActions.Write, StringComparison.OrdinalIgnoreCase))
        {
            return IngestionWriteDenied;
        }

        return Default(resource, action);
    }
}
