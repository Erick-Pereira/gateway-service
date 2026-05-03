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
using AuthZMiddleware = Simcag.Gateway.Infrastructure.Middleware.AuthorizationMiddleware;

DotNetEnv.Env.Load();
var builder = WebApplication.CreateBuilder(args);

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

// Downstream health checks — Degraded (não Unhealthy) para que o gateway retorne 200
// mesmo com serviços offline. Em produção use dashboards de observabilidade.
static void AddDownstream(IHealthChecksBuilder b, string url, string name)
    => b.AddUrlGroup(new Uri($"{url}/health"), name: name,
           failureStatus: Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded,
           tags: ["downstream"]);

AddDownstream(health, identityServiceUrl,      "identity-service");
AddDownstream(health, ingestionServiceUrl,     "ingestion-service");
AddDownstream(health, processingServiceUrl,    "processing-service");
AddDownstream(health, alertServiceUrl,         "alert-service");
AddDownstream(health, notificationServiceUrl,  "notification-service");
AddDownstream(health, priceAnalysisServiceUrl, "price-analysis-service");
AddDownstream(health, marketDataServiceUrl,    "market-data-service");
AddDownstream(health, aiServiceUrl,            "ai-service");

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

// Middleware pipeline
if (app.Environment.IsDevelopment())
{
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
}

app.UseRouting();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseMiddleware<RateLimitingMiddleware>();

// Arquivos estáticos (wwwroot) e "/" → index.html — deve vir antes do middleware de autenticação
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseMiddleware<AuthenticationMiddleware>();
app.UseMiddleware<AuthZMiddleware>();
app.UseMiddleware<ResponseCachingMiddleware>();
app.UseMiddleware<ResponseFormatMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// YARP Proxy
app.MapReverseProxy();

app.Run();

file static class DevJwtSecretFallback
{
    // Manter o literal idêntico em identity-service/Simcag.IdentityService.Api/Program.cs (só Development).
    public const string Value = "Simcag.Dev.Jwt.NotForProduction.AlignWithIdentityService.01!";
}
