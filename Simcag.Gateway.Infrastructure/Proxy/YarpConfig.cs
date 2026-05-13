using Yarp.ReverseProxy.Configuration;

namespace Simcag.Gateway.Infrastructure.Proxy;

public static class YarpConfig
{
    /// <summary>Endereço base (URL) a partir de variáveis de ambiente; a última string é o default.</summary>
    public static string ClusterAddress(string fallback, params string[] envKeys)
    {
        foreach (var k in envKeys)
        {
            var v = Environment.GetEnvironmentVariable(k);
            if (!string.IsNullOrWhiteSpace(v))
                return v!.Trim();
        }
        return fallback;
    }

    public static IEnumerable<RouteConfig> GetRoutes() =>
        new[]
        {
            new RouteConfig
            {
                RouteId = "auth-route",
                ClusterId = "admin-cluster",
                Match = new RouteMatch { Path = "/api/auth/{**catch-all}" }
            },
            new RouteConfig
            {
                RouteId = "ingestion-route",
                ClusterId = "ingestion-cluster",
                Match = new RouteMatch { Path = "/api/ingestion/{**catch-all}" }
            },
            new RouteConfig
            {
                RouteId = "alert-route",
                ClusterId = "alert-cluster",
                Match = new RouteMatch { Path = "/api/alerts/{**catch-all}" }
            },
            // ASP.NET [Route("api/[controller]")] → template api/Alerts; o host aceita /api/alerts (case-insensitive).
            // Não duplicar api/Alerts no YARP — gera AmbiguousMatchException para GET /api/alerts.
            new RouteConfig
            {
                RouteId = "alert-rules-route",
                ClusterId = "alert-cluster",
                Match = new RouteMatch { Path = "/api/AlertRules/{**catch-all}" }
            },
            new RouteConfig
            {
                RouteId = "notification-route",
                ClusterId = "notification-cluster",
                Match = new RouteMatch { Path = "/api/notifications/{**catch-all}" }
            },
            new RouteConfig
            {
                RouteId = "audit-logs-route",
                ClusterId = "processing-cluster",
                Match = new RouteMatch { Path = "/api/audit-logs/{**catch-all}" },
                AuthorizationPolicy = "AdminOnly"
            },
            new RouteConfig
            {
                RouteId = "payments-route",
                ClusterId = "processing-cluster",
                Match = new RouteMatch { Path = "/api/payments/{**catch-all}" }
            },
            new RouteConfig
            {
                RouteId = "reports-route",
                ClusterId = "processing-cluster",
                Match = new RouteMatch { Path = "/api/reports/{**catch-all}" }
            },
            // Refresh da MView do processing — path absoluto no downstream; deve ir ao processing, não ao identity.
            new RouteConfig
            {
                RouteId = "processing-admin-refresh-dashboard-route",
                ClusterId = "processing-cluster",
                Match = new RouteMatch { Path = "/api/admin/refresh-dashboard" },
                AuthorizationPolicy = "AdminOnly"
            },
            new RouteConfig
            {
                RouteId = "admin-route",
                ClusterId = "admin-cluster",
                Match = new RouteMatch { Path = "/api/admin/{**catch-all}" },
                AuthorizationPolicy = "AdminOnly",
                Transforms = new[] { new Dictionary<string, string> { { "PathRemovePrefix", "/api/admin" } } }
            },
            new RouteConfig
            {
                RouteId = "condominios-route",
                ClusterId = "admin-cluster",
                Match = new RouteMatch { Path = "/api/condominios/{**catch-all}" }
            },
            new RouteConfig
            {
                RouteId = "expenses-route",
                ClusterId = "processing-cluster",
                Match = new RouteMatch { Path = "/api/expenses/{**catch-all}" }
            },
            new RouteConfig
            {
                RouteId = "suppliers-merge-route",
                ClusterId = "processing-cluster",
                Match = new RouteMatch { Path = "/api/suppliers/merge" },
                AuthorizationPolicy = "AdminOnly"
            },
            new RouteConfig
            {
                RouteId = "suppliers-route",
                ClusterId = "processing-cluster",
                Match = new RouteMatch { Path = "/api/suppliers/{**catch-all}" }
            },
            new RouteConfig
            {
                RouteId = "price-analysis-route",
                ClusterId = "price-analysis-cluster",
                Match = new RouteMatch { Path = "/api/price-analysis/{**catch-all}" }
            },
            new RouteConfig
            {
                RouteId = "price-analysis-pascal-route",
                ClusterId = "price-analysis-cluster",
                Match = new RouteMatch { Path = "/api/PriceAnalysis/{**catch-all}" }
            },
            new RouteConfig
            {
                RouteId = "market-data-route",
                ClusterId = "market-data-cluster",
                Match = new RouteMatch { Path = "/api/market-data/{**catch-all}" }
            },
            new RouteConfig
            {
                RouteId = "market-data-pascal-route",
                ClusterId = "market-data-cluster",
                Match = new RouteMatch { Path = "/api/MarketData/{**catch-all}" }
            },
            new RouteConfig
            {
                RouteId = "ai-route",
                ClusterId = "ai-cluster",
                Match = new RouteMatch { Path = "/api/ai/{**catch-all}" }
            },
            new RouteConfig
            {
                RouteId = "dashboard-route",
                ClusterId = "processing-cluster",
                Match = new RouteMatch { Path = "/api/dashboard/{**catch-all}" }
            },
            // -- Documentos OpenAPI dos serviços downstream (acessíveis pelo Swagger UI do gateway) --
            DocsRoute("identity-docs-route", "admin-cluster", "/api/identity-docs"),
            DocsRoute("ingestion-docs-route", "ingestion-cluster", "/api/ingestion-docs"),
            DocsRoute("processing-docs-route", "processing-cluster", "/api/processing-docs"),
            DocsRoute("alert-docs-route", "alert-cluster", "/api/alert-docs"),
            DocsRoute("notification-docs-route", "notification-cluster", "/api/notification-docs"),
            DocsRoute("price-analysis-docs-route", "price-analysis-cluster", "/api/price-analysis-docs"),
            DocsRoute("market-data-docs-route", "market-data-cluster", "/api/market-data-docs"),
            DocsRoute("ai-docs-route", "ai-cluster", "/api/ai-docs")
        };

    private static RouteConfig DocsRoute(string routeId, string clusterId, string prefix) => new()
    {
        RouteId = routeId,
        ClusterId = clusterId,
        Match = new RouteMatch { Path = $"{prefix}/{{**catch-all}}" },
        Transforms = new[] { new Dictionary<string, string> { { "PathRemovePrefix", prefix } } }
    };

    public static IEnumerable<ClusterConfig> GetClusters() =>
        new[]
        {
            Cluster("ingestion-cluster", "ingestion",
                "http://ingestion-service:8081",
                "GATEWAY_INGESTION_ADDRESS", "SERVICES__INGESTION__URL", "INGESTION_SERVICE_URL"),

            Cluster("alert-cluster", "alert",
                "http://alert-service:8083",
                "GATEWAY_ALERT_ADDRESS", "SERVICES__ALERT__URL", "ALERT_SERVICE_URL"),

            Cluster("notification-cluster", "notification",
                "http://notification-service:8084",
                "GATEWAY_NOTIFICATION_ADDRESS", "SERVICES__NOTIFICATION__URL", "NOTIFICATION_SERVICE_URL"),

            Cluster("processing-cluster", "processing",
                "http://processing-service:8082",
                "GATEWAY_PROCESSING_ADDRESS", "SERVICES__PROCESSING__URL", "PROCESSING_SERVICE_URL"),

            Cluster("admin-cluster", "identity",
                "http://identity-service:8080",
                "GATEWAY_IDENTITY_ADDRESS", "SERVICES__IDENTITY__URL", "IDENTITY_SERVICE_URL"),

            Cluster("price-analysis-cluster", "price-analysis",
                "http://price-analysis-service:8086",
                "GATEWAY_PRICE_ANALYSIS_ADDRESS", "SERVICES__PRICE_ANALYSIS__URL", "PRICE_ANALYSIS_SERVICE_URL"),

            Cluster("market-data-cluster", "market-data",
                "http://market-data-service:8085",
                "GATEWAY_MARKET_DATA_ADDRESS", "SERVICES__MARKET_DATA__URL", "MARKET_DATA_SERVICE_URL"),

            Cluster("ai-cluster", "ai",
                "http://ai-service:8087",
                "GATEWAY_AI_ADDRESS", "SERVICES__AI__URL", "AI_SERVICE_URL"),
        };

    private static ClusterConfig Cluster(string clusterId, string destinationKey, string fallback, params string[] envKeys) =>
        new()
        {
            ClusterId = clusterId,
            Destinations = new Dictionary<string, DestinationConfig>
            {
                [destinationKey] = new() { Address = ClusterAddress(fallback, envKeys) }
            }
        };
}
