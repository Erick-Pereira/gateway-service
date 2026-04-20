using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Threading.RateLimiting;

DotNetEnv.Env.Load();

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

// Configure YARP Reverse Proxy
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// Configure JWT Authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = Environment.GetEnvironmentVariable("JWT__ISSUER") ?? "Simcag.IdentityService",
            ValidAudience = Environment.GetEnvironmentVariable("JWT__AUDIENCE") ?? "Simcag.Clients",
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(
                    Environment.GetEnvironmentVariable("JWT__KEY") ??
                    "your-super-secure-jwt-key-here-at-least-256-bits-long"))
        };
    });

// Configure CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(
                Environment.GetEnvironmentVariable("CORS__ALLOWED_ORIGINS")?.Split(',') ??
                new[] { "http://localhost:3000", "http://localhost:5173" })
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});

// Add rate limiting
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("default", context =>
    {
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            });
    });
});

// Add health checks
builder.Services.AddHealthChecks();

// Add Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Use CORS
app.UseCors("AllowFrontend");

// Use rate limiting
app.UseRateLimiter();

// Use authentication and authorization
app.UseAuthentication();
app.UseAuthorization();

// Add custom JWT validation middleware
app.UseJwtValidation();

// Add custom error handling for JWT validation failures
app.Use(async (context, next) =>
{
    await next();

    // Handle JWT validation errors
    if (context.Response.StatusCode == StatusCodes.Status401Unauthorized)
    {
        if (!context.Response.HasStarted)
        {
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                success = false,
                error = "Unauthorized access",
                code = "InvalidToken",
                message = "Please provide a valid JWT token"
            });
        }
    }
});

// Map reverse proxy routes
app.MapReverseProxy();

// Health check endpoint
app.MapHealthChecks("/health");

// Fallback for unmatched routes
app.MapFallbackToFile("index.html");

app.Run();