using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Serilog;
using Simcag.Gateway.Infrastructure.Configuration;
using Simcag.Gateway.Infrastructure.Middleware;
using Simcag.Gateway.Api.Middleware;
using DotNetEnv;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using AuthZMiddleware = Simcag.Gateway.Infrastructure.Middleware.AuthorizationMiddleware;

// Não sobrescrever variáveis já definidas (Docker/Portainer): .env local é só fallback.
DotNetEnv.Env.NoClobber().Load();

// Docker: o mesmo .env que no PC (ASPNETCORE_URLS=http://localhost:5000) faz o Kestrel escutar só em loopback
// dentro do contentor — o mapeamento potato-server:5000 não chega à app. Reescrever para http://+:porta.
if (string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase))
{
    var aspNetUrls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
    if (!string.IsNullOrWhiteSpace(aspNetUrls) && AllAspNetCoreUrlSegmentsAreLoopback(aspNetUrls))
    {
        var fromLoopbackUrl = GatewayFirstHttpListenPortFromLoopbackAspNetCoreUrls(aspNetUrls);
        var fromHttpPorts = GatewayParseFirstAspNetCoreHttpPort();
        var listenPort = fromLoopbackUrl ?? fromHttpPorts ?? 8080;
        Environment.SetEnvironmentVariable("ASPNETCORE_URLS", $"http://+:{listenPort}");
    }
    else if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS")))
    {
        var httpPortsRaw = Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS");
        var firstPortToken = httpPortsRaw!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (int.TryParse(firstPortToken, out var expectedPort) && expectedPort > 0
            && !GatewayFirstHttpListenIsCompatible(expectedPort))
            Environment.SetEnvironmentVariable("ASPNETCORE_URLS", $"http://+:{expectedPort}");
    }
}

var builder = WebApplication.CreateBuilder(args);
GatewayApplyDockerListenUrls(builder);
GatewayRewriteLoopbackDownstreamServiceEnvarsInContainer();
GatewaySynthesizeDownstreamServiceUrlsFromHostAndPorts();
GatewayFallbackDownstreamToDockerHostWhenComposeDnsMissing();
GatewayWarnIfMisconfiguredDownstreamLoopbackInContainer();

static string? GetEnv(params string[] keys)
{
    foreach (var key in keys)
    {
        var value = Environment.GetEnvironmentVariable(key);
        if (!string.IsNullOrWhiteSpace(value))
            return value;
    }
    return null;
}

var identityServiceUrl      = GetEnv("SERVICES__IDENTITY__URL",      "IDENTITY_SERVICE_URL")      ?? "http://identity-service:8080";
var ingestionServiceUrl     = GetEnv("SERVICES__INGESTION__URL",     "INGESTION_SERVICE_URL")     ?? "http://ingestion-service:8081";
var processingServiceUrl    = GetEnv("SERVICES__PROCESSING__URL",    "PROCESSING_SERVICE_URL")    ?? "http://processing-service:8082";
var alertServiceUrl         = GetEnv("SERVICES__ALERT__URL",         "ALERT_SERVICE_URL")         ?? "http://alert-service:8083";
var notificationServiceUrl  = GetEnv("SERVICES__NOTIFICATION__URL",  "NOTIFICATION_SERVICE_URL")  ?? "http://notification-service:8084";
var priceAnalysisServiceUrl = GetEnv("SERVICES__PRICE_ANALYSIS__URL","PRICE_ANALYSIS_SERVICE_URL")?? "http://price-analysis-service:8086";
var marketDataServiceUrl    = GetEnv("SERVICES__MARKET_DATA__URL",   "MARKET_DATA_SERVICE_URL")   ?? "http://market-data-service:8085";
var aiServiceUrl            = GetEnv("SERVICES__AI__URL",            "AI_SERVICE_URL")            ?? "http://ai-service:8087";

GatewayLogImplicitDownstreamResolutionMode();

var redisForCache = DependencyInjection.GetRedisCacheConnection();
var useInMemoryCache = DependencyInjection.IsInProcessDistributedCache(redisForCache);

// O mesmo segredo/issuer/audience do Identity (JWT HMAC) — o gateway não usa OIDC discovery
var jwtSecret = GetEnv("JWT__SECRET", "JWT_SECRET", "JWT_SECRETKEY");
if (string.IsNullOrWhiteSpace(jwtSecret))
{
    if (!builder.Environment.IsDevelopment())
        throw new InvalidOperationException("Defina JWT__SECRET (alinhado ao serviço Identity).");
    jwtSecret = DevJwtSecretFallback.Value;
    Console.WriteLine(
        "[Simcag.Gateway] JWT__SECRET ausente: a usar segredo fixo só para Development. Defina JWT__SECRET alinhado ao Identity fora de Development.");
}

var jwtIssuer = GetEnv("JWT__ISSUER", "Jwt__Issuer") ?? "Simcag.IdentityService";
var jwtAudience = GetEnv("JWT__AUDIENCE", "Jwt__Audience") ?? "Simcag.Clients";

// Logging (configuração via .env, não appsettings)
builder.Host.UseSerilog((ctx, lc) => lc
    .WriteTo.Console()
    .Enrich.FromLogContext()
    .MinimumLevel.Information());

// Serviços
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    });

// Authentication (JWT) — assinatura simétrica, igual ao Identity; roles: ADMIN, SINDICO, CONSELHO
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            // Os tokens (e o Domain.Role) usam "ADMIN/SINDICO/CONSELHO".
            // Também suportamos o claim custom "role" além do ClaimTypes.Role.
            RoleClaimType = "role"
        };
        options.RequireHttpsMetadata = builder.Environment.IsProduction();
    });

builder.Services.AddAuthorization(options =>
{
    // O AuthenticationMiddleware do gateway converte os roles do JWT (ex: "Admin")
    // para o nome do enum (ex: "ADMIN") via Role.ToString(). As policies usam o nome do enum.
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("ADMIN"));
    options.AddPolicy("SindicoOnly", policy => policy.RequireRole("SINDICO", "ADMIN"));
});

// Infrastructure (Redis, HttpClient, Services, YARP)
builder.Services.AddInfrastructure();

// Health Checks (Redis só se estiver configurado; sem variável = cache em memória no AddInfrastructure)
var health = builder.Services.AddHealthChecks();
if (!useInMemoryCache && !string.IsNullOrEmpty(redisForCache))
    health.AddRedis(redisForCache, name: "redis");

// Downstream health checks — Degraded; timeout curto para /health não bloquear ~10s por URI (default do HttpClient).
static TimeSpan GatewayDownstreamHealthCheckTimeout()
{
    var raw = Environment.GetEnvironmentVariable("GATEWAY_DOWNSTREAM_HEALTH_TIMEOUT_SECONDS");
    if (int.TryParse(raw, out var sec) && sec > 0 && sec <= 120)
        return TimeSpan.FromSeconds(sec);
    return TimeSpan.FromSeconds(3);
}

static bool GatewaySkipDownstreamHealthChecks()
{
    var v = Environment.GetEnvironmentVariable("GATEWAY_SKIP_DOWNSTREAM_HEALTH");
    return string.Equals(v, "1", StringComparison.Ordinal)
        || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
}

static void AddDownstream(IHealthChecksBuilder b, string url, string name)
{
    var t = GatewayDownstreamHealthCheckTimeout();
    b.AddUrlGroup(
        new Uri($"{url.TrimEnd('/')}/health"),
        name: name,
        failureStatus: Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded,
        tags: ["downstream"],
        timeout: t,
        configureClient: (_, client) => client.Timeout = t);
}

if (!GatewaySkipDownstreamHealthChecks())
{
    AddDownstream(health, identityServiceUrl,      "identity-service");
    AddDownstream(health, ingestionServiceUrl,     "ingestion-service");
    AddDownstream(health, processingServiceUrl,    "processing-service");
    AddDownstream(health, alertServiceUrl,         "alert-service");
    AddDownstream(health, notificationServiceUrl,  "notification-service");
    AddDownstream(health, priceAnalysisServiceUrl, "price-analysis-service");
    AddDownstream(health, marketDataServiceUrl,    "market-data-service");
    AddDownstream(health, aiServiceUrl,            "ai-service");
}

// Swagger (Swashbuckle 10.x + Microsoft.OpenApi 2.x — API delegada)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "SIMC-AG Gateway",
        Version = "v1",
        Description = "API Gateway do SIMC-AG (auditoria condominial). Use o botão Authorize com um JWT do Identity Service para testar rotas autenticadas."
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Cole apenas o token JWT (sem o prefixo Bearer)."
    });

    c.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = []
    });
});

// (Métricas / Prometheus) — reintroduza IServiceCollection.AddMetrics + MapMetrics com o pacote adequado, se necessário

// Use Cases (DI) — sem use cases de proxy: o YARP faz o roteamento.

var app = builder.Build();

// Middleware pipeline — única UI HTTP: Swagger (sem wwwroot / index.html que interceptavam pedidos).
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Gateway");
    // Documentos OpenAPI dos serviços downstream (proxy em /api/<serviço>-docs/... → /swagger/v1/swagger.json).
    options.SwaggerEndpoint("/api/identity-docs/swagger/v1/swagger.json", "Identity");
    options.SwaggerEndpoint("/api/ingestion-docs/swagger/v1/swagger.json", "Ingestion");
    options.SwaggerEndpoint("/api/processing-docs/swagger/v1/swagger.json", "Processing");
    options.SwaggerEndpoint("/api/alert-docs/swagger/v1/swagger.json", "Alert");
    options.SwaggerEndpoint("/api/notification-docs/swagger/v1/swagger.json", "Notification");
    options.SwaggerEndpoint("/api/price-analysis-docs/swagger/v1/swagger.json", "Price Analysis");
    options.SwaggerEndpoint("/api/market-data-docs/swagger/v1/swagger.json", "Market Data");
    options.SwaggerEndpoint("/api/ai-docs/swagger/v1/swagger.json", "IA (AI Service)");
    options.RoutePrefix = "swagger";
    options.DocumentTitle = "SIMC-AG Gateway — Swagger";
});

app.UseRouting();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseMiddleware<RateLimitingMiddleware>();

app.UseMiddleware<AuthenticationMiddleware>();
app.UseMiddleware<AuthZMiddleware>();
app.UseMiddleware<ResponseCachingMiddleware>();
app.UseMiddleware<ResponseFormatMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Liveness para Docker HEALTHCHECK: sem probes HTTP aos downstreams (evita timeout 3s do Docker vs /health ~3s+).
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = registration => registration.Tags is null || !registration.Tags.Contains("downstream"),
});

// YARP Proxy
app.MapReverseProxy();

app.Run();

/// <summary>
/// Em container, URLs com localhost apontam para o próprio gateway. Se SIMCAG_DOWNSTREAM_HOST (ou GATEWAY_DOWNSTREAM_HOST)
/// estiver definido (ex.: host.docker.internal, nome DNS do host, IP da bridge), reescreve todas as variáveis de destino
/// usadas pelo YARP e pelos health checks downstream.
/// </summary>
static void GatewayRewriteLoopbackDownstreamServiceEnvarsInContainer()
{
    if (!string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase))
        return;

    var replacementHost = (Environment.GetEnvironmentVariable("SIMCAG_DOWNSTREAM_HOST")
        ?? Environment.GetEnvironmentVariable("GATEWAY_DOWNSTREAM_HOST"))?.Trim();
    if (string.IsNullOrEmpty(replacementHost))
        return;

    if (Uri.TryCreate($"http://{replacementHost}/", UriKind.Absolute, out var probe) && probe.IsLoopback)
        return;

    foreach (var key in GatewayDownstreamEnvKeys.All)
    {
        var v = Environment.GetEnvironmentVariable(key);
        if (string.IsNullOrWhiteSpace(v))
            continue;
        if (!GatewayTryReplaceLoopbackHostInBaseUrl(v.Trim(), replacementHost, out var rewritten))
            continue;
        Environment.SetEnvironmentVariable(key, rewritten);
    }
}

/// <summary>
/// Contentor só na bridge: identity-service não existe no DNS → os defaults *-service:808x falham.
/// Se nenhuma URL downstream estiver definida e identity-service não resolver, assume APIs publicadas
/// no host (portas 5001–5008) via SIMCAG_FALLBACK_HOST (default host.docker.internal).
/// </summary>
static void GatewayFallbackDownstreamToDockerHostWhenComposeDnsMissing()
{
    if (!string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase))
        return;

    if (string.Equals(Environment.GetEnvironmentVariable("SIMCAG_DISABLE_HOST_PORT_FALLBACK"), "1", StringComparison.Ordinal)
        || string.Equals(Environment.GetEnvironmentVariable("SIMCAG_DISABLE_HOST_PORT_FALLBACK"), "true", StringComparison.OrdinalIgnoreCase))
        return;

    if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SIMCAG_SERVICES_HOST")))
        return;

    foreach (var key in GatewayDownstreamEnvKeys.All)
    {
        if (GatewayEnvLooksLikeAbsoluteUrl(Environment.GetEnvironmentVariable(key)))
            return;
    }

    if (GatewayTryResolveHost("identity-service"))
        return;

    var fallbackHost = GatewayResolveFallbackHostForHostPublishedPorts();

    foreach (var row in GatewayDownstreamUrlSynthesis.Rows)
        Environment.SetEnvironmentVariable(row.Primary, $"http://{fallbackHost}:{row.DefaultPort}");

    Console.WriteLine(
        "[Simcag.Gateway] identity-service não resolve neste contentor — a usar http://" + fallbackHost
        + ":5001–5008 (APIs no host). Rede não-default / rootless: defina SIMCAG_FALLBACK_HOST. "
        + "Compose com DNS: SIMCAG_DISABLE_HOST_PORT_FALLBACK=1 ou SERVICES__*__URL.");
}

/// <summary>
/// Ordem: SIMCAG_FALLBACK_HOST; senão host.docker.internal se existir no DNS (Docker Desktop);
/// senão 172.17.0.1 (gateway típico da bridge docker0 no Linux).
/// </summary>
static string GatewayResolveFallbackHostForHostPublishedPorts()
{
    var custom = Environment.GetEnvironmentVariable("SIMCAG_FALLBACK_HOST")?.Trim();
    if (!string.IsNullOrEmpty(custom))
        return custom;

    if (GatewayTryResolveHost("host.docker.internal"))
        return "host.docker.internal";

    return "172.17.0.1";
}

static bool GatewayTryResolveHost(string hostname)
{
    try
    {
        _ = System.Net.Dns.GetHostEntry(hostname);
        return true;
    }
    catch
    {
        return false;
    }
}

static void GatewayWarnIfMisconfiguredDownstreamLoopbackInContainer()
{
    if (!string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase))
        return;

    if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SIMCAG_SERVICES_HOST"))
        || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SIMCAG_DOWNSTREAM_HOST"))
        || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GATEWAY_DOWNSTREAM_HOST")))
        return;

    foreach (var key in GatewayDownstreamEnvKeys.PrimaryServices)
    {
        var v = Environment.GetEnvironmentVariable(key);
        if (string.IsNullOrWhiteSpace(v))
            continue;
        if (!Uri.TryCreate(v.Trim(), UriKind.Absolute, out var uri) || !uri.IsLoopback)
            continue;

        Console.WriteLine(
            "[Simcag.Gateway] Em container, " + key + " usa localhost — isso aponta para o próprio gateway. " +
            "Defina SIMCAG_SERVICES_HOST ou SIMCAG_DOWNSTREAM_HOST (hostname alcançável), " +
            "ou URLs completas em SERVICES__*__URL, ou SIMCAG_SERVICES_HOST (mapa de portas dev 5001–5008).");
        return;
    }
}

static bool GatewayTryReplaceLoopbackHostInBaseUrl(string url, string newHost, out string? rewritten)
{
    rewritten = null;
    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        return false;
    if (!uri.IsLoopback)
        return false;

    var builder = new UriBuilder(uri) { Host = newHost };
    rewritten = builder.Uri.ToString().TrimEnd('/');
    return true;
}

/// <summary>
/// Preenche SERVICES__*__URL em falta com {SIMCAG_SERVICES_SCHEME}://{host}:{porta}.
/// Host: SIMCAG_SERVICES_HOST, senão SIMCAG_DOWNSTREAM_HOST / GATEWAY_DOWNSTREAM_HOST;
/// fora de contentor em Development, default localhost. Em contentor, é obrigatório definir um host alcançável.
/// Portas no modo host único: mapa fixo dev 5001–5008 (simcag/.env.example). Em Docker na rede Compose, omita URLs e use os defaults do código (identity-service:8080, …).
/// </summary>
static void GatewaySynthesizeDownstreamServiceUrlsFromHostAndPorts()
{
    var inContainer = string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase);
    var aspNetEnv = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
    var isDevelopment = string.IsNullOrWhiteSpace(aspNetEnv)
        || string.Equals(aspNetEnv, "Development", StringComparison.OrdinalIgnoreCase);

    var host = (Environment.GetEnvironmentVariable("SIMCAG_SERVICES_HOST")
        ?? Environment.GetEnvironmentVariable("SIMCAG_DOWNSTREAM_HOST")
        ?? Environment.GetEnvironmentVariable("GATEWAY_DOWNSTREAM_HOST"))?.Trim();

    if (string.IsNullOrEmpty(host))
    {
        if (inContainer || !isDevelopment)
            return;
        host = "localhost";
    }

    var scheme = GatewayNormalizeServicesScheme();

    foreach (var row in GatewayDownstreamUrlSynthesis.Rows)
    {
        if (GatewaySynthesisRowHasAbsoluteUrl(row))
            continue;

        var url = $"{scheme}://{host}:{row.DefaultPort}";
        Environment.SetEnvironmentVariable(row.Primary, url);
    }
}

static bool GatewaySynthesisRowHasAbsoluteUrl((string Primary, int DefaultPort, string[] UrlKeys) row)
{
    if (GatewayEnvLooksLikeAbsoluteUrl(Environment.GetEnvironmentVariable(row.Primary)))
        return true;
    foreach (var k in row.UrlKeys)
    {
        if (GatewayEnvLooksLikeAbsoluteUrl(Environment.GetEnvironmentVariable(k)))
            return true;
    }

    return false;
}

static bool GatewayEnvLooksLikeAbsoluteUrl(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
        return false;
    var t = value.Trim();
    return t.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || t.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// O YARP faz de reverse proxy HTTP para estes destinos. Os nomes identity-service, … só resolvem
/// se existirem contentores com esses nomes na mesma rede Docker (típico docker compose). Um único
/// contentor gateway na bridge por defeito não vê esses DNS — use SIMCAG_SERVICES_HOST ou SERVICES__*__URL.
/// </summary>
static void GatewayLogImplicitDownstreamResolutionMode()
{
    if (!string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase))
        return;

    if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SIMCAG_SERVICES_HOST")))
        return;

    foreach (var key in GatewayDownstreamEnvKeys.All)
    {
        if (GatewayEnvLooksLikeAbsoluteUrl(Environment.GetEnvironmentVariable(key)))
            return;
    }

    Console.WriteLine(
        "[Simcag.Gateway] Nenhuma URL downstream nas variáveis de ambiente: a usar os defaults do código " +
        "(http://identity-service:8080, http://ingestion-service:8081, …). Isto só funciona na mesma rede " +
        "que esses contentores (ex.: stack Compose). Se só corre o gateway, defina SIMCAG_SERVICES_HOST " +
        "(ex.: host.docker.internal) ou SERVICES__*__URL com endereços alcançáveis.");
}

static string GatewayNormalizeServicesScheme()
{
    var s = Environment.GetEnvironmentVariable("SIMCAG_SERVICES_SCHEME")?.Trim();
    if (string.IsNullOrEmpty(s))
        return "http";
    if (s.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        return "https";
    if (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        return "http";
    if (s.Equals("https", StringComparison.OrdinalIgnoreCase))
        return "https";
    if (s.Equals("http", StringComparison.OrdinalIgnoreCase))
        return "http";
    return "http";
}

static void GatewayApplyDockerListenUrls(WebApplicationBuilder builder)
{
    if (!string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase))
        return;

    var urlsNow = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
    if (!string.IsNullOrWhiteSpace(urlsNow) && GatewayAspNetCoreUrlsUseAnyNetworkInterface(urlsNow))
        return;

    var httpPorts = Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS");
    if (string.IsNullOrWhiteSpace(httpPorts))
        return;

    var firstPortToken = httpPorts.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
    if (!int.TryParse(firstPortToken, out var expectedPort) || expectedPort <= 0)
        return;

    if (GatewayFirstHttpListenIsCompatible(expectedPort))
        return;

    Environment.SetEnvironmentVariable("ASPNETCORE_URLS", null);
    builder.WebHost.UseSetting(WebHostDefaults.ServerUrlsKey, $"http://+:{expectedPort}");
}

static bool GatewayFirstHttpListenIsCompatible(int expectedPort)
{
    var urls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
    if (string.IsNullOrWhiteSpace(urls))
        return false;

    foreach (var segment in urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        if (!Uri.TryCreate(segment, UriKind.Absolute, out var uri))
            return false;
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
            continue;

        return uri.Port == expectedPort && GatewayIsAcceptableListenHost(uri);
    }

    return false;
}

static bool GatewayIsAcceptableListenHost(Uri uri)
{
    if (uri.IsLoopback)
        return true;

    return uri.Host switch
    {
        "+" or "*" or "0.0.0.0" or "[::]" => true,
        "::" => true,
        _ => false
    };
}

static bool AllAspNetCoreUrlSegmentsAreLoopback(string aspNetCoreUrls)
{
    foreach (var segment in aspNetCoreUrls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        if (!Uri.TryCreate(segment, UriKind.Absolute, out var uri) || !uri.IsLoopback)
            return false;
    }

    return true;
}

static bool GatewayAspNetCoreUrlsUseAnyNetworkInterface(string aspNetCoreUrls)
{
    foreach (var segment in aspNetCoreUrls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        if (!Uri.TryCreate(segment, UriKind.Absolute, out var uri))
            continue;
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
            continue;
        return GatewayIsAcceptableListenHost(uri);
    }

    return false;
}

static int? GatewayParseFirstAspNetCoreHttpPort()
{
    var raw = Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS");
    if (string.IsNullOrWhiteSpace(raw))
        return null;
    var token = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
    return int.TryParse(token, out var p) && p > 0 ? p : null;
}

static int? GatewayFirstHttpListenPortFromLoopbackAspNetCoreUrls(string aspNetCoreUrls)
{
    foreach (var segment in aspNetCoreUrls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        if (!Uri.TryCreate(segment, UriKind.Absolute, out var uri))
            continue;
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
            continue;
        if (!uri.IsLoopback)
            return null;
        if (uri.Port < 1)
            continue;
        return uri.Port;
    }

    return null;
}

file static class DevJwtSecretFallback
{
    // Manter o literal idêntico em identity-service/Simcag.IdentityService.Api/Program.cs (só Development).
    public const string Value = "Simcag.Dev.Jwt.NotForProduction.AlignWithIdentityService.01!";
}

/// <summary>Chaves consultadas por YarpConfig.ClusterAddress (precedência entre chaves não importa para o rewrite).</summary>
file static class GatewayDownstreamUrlSynthesis
{
    /// <summary>Primary URL env, port override env, default dev port, aliases checked antes de sintetizar.</summary>
    public static readonly (string Primary, int DefaultPort, string[] UrlKeys)[] Rows =
    [
        ("SERVICES__IDENTITY__URL", 5001, ["IDENTITY_SERVICE_URL", "GATEWAY_IDENTITY_ADDRESS"]),
        ("SERVICES__INGESTION__URL", 5002, ["INGESTION_SERVICE_URL", "GATEWAY_INGESTION_ADDRESS"]),
        ("SERVICES__PROCESSING__URL", 5003, ["PROCESSING_SERVICE_URL", "GATEWAY_PROCESSING_ADDRESS"]),
        ("SERVICES__ALERT__URL", 5004, ["ALERT_SERVICE_URL", "GATEWAY_ALERT_ADDRESS"]),
        ("SERVICES__NOTIFICATION__URL", 5005, ["NOTIFICATION_SERVICE_URL", "GATEWAY_NOTIFICATION_ADDRESS"]),
        ("SERVICES__PRICE_ANALYSIS__URL", 5006, ["PRICE_ANALYSIS_SERVICE_URL", "GATEWAY_PRICE_ANALYSIS_ADDRESS"]),
        ("SERVICES__MARKET_DATA__URL", 5007, ["MARKET_DATA_SERVICE_URL", "GATEWAY_MARKET_DATA_ADDRESS"]),
        ("SERVICES__AI__URL", 5008, ["AI_SERVICE_URL", "GATEWAY_AI_ADDRESS"]),
    ];
}

file static class GatewayDownstreamEnvKeys
{
    public static readonly string[] All =
    [
        "SERVICES__IDENTITY__URL", "IDENTITY_SERVICE_URL", "GATEWAY_IDENTITY_ADDRESS",
        "SERVICES__INGESTION__URL", "INGESTION_SERVICE_URL", "GATEWAY_INGESTION_ADDRESS",
        "SERVICES__ALERT__URL", "ALERT_SERVICE_URL", "GATEWAY_ALERT_ADDRESS",
        "SERVICES__NOTIFICATION__URL", "NOTIFICATION_SERVICE_URL", "GATEWAY_NOTIFICATION_ADDRESS",
        "SERVICES__PROCESSING__URL", "PROCESSING_SERVICE_URL", "GATEWAY_PROCESSING_ADDRESS",
        "SERVICES__PRICE_ANALYSIS__URL", "PRICE_ANALYSIS_SERVICE_URL", "GATEWAY_PRICE_ANALYSIS_ADDRESS",
        "SERVICES__MARKET_DATA__URL", "MARKET_DATA_SERVICE_URL", "GATEWAY_MARKET_DATA_ADDRESS",
        "SERVICES__AI__URL", "AI_SERVICE_URL", "GATEWAY_AI_ADDRESS",
    ];

    public static readonly string[] PrimaryServices =
    [
        "SERVICES__IDENTITY__URL",
        "SERVICES__INGESTION__URL",
        "SERVICES__ALERT__URL",
        "SERVICES__NOTIFICATION__URL",
        "SERVICES__PROCESSING__URL",
        "SERVICES__PRICE_ANALYSIS__URL",
        "SERVICES__MARKET_DATA__URL",
        "SERVICES__AI__URL",
    ];
}
