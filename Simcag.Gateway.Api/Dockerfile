# Multi-stage — contexto: pasta gateway-service (sem Simcag.Shared / sem TOKEN).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS builder

WORKDIR /src

ARG BUILD_CONFIGURATION=Release

COPY ["Simcag.Gateway.Api/Simcag.Gateway.Api.csproj", "Simcag.Gateway.Api/"]
COPY ["Simcag.Gateway.Application/Simcag.Gateway.Application.csproj", "Simcag.Gateway.Application/"]
COPY ["Simcag.Gateway.Domain/Simcag.Gateway.Domain.csproj", "Simcag.Gateway.Domain/"]
COPY ["Simcag.Gateway.Infrastructure/Simcag.Gateway.Infrastructure.csproj", "Simcag.Gateway.Infrastructure/"]

RUN dotnet restore "Simcag.Gateway.Api/Simcag.Gateway.Api.csproj"

COPY ["Simcag.Gateway.Api/", "Simcag.Gateway.Api/"]
COPY ["Simcag.Gateway.Application/", "Simcag.Gateway.Application/"]
COPY ["Simcag.Gateway.Domain/", "Simcag.Gateway.Domain/"]
COPY ["Simcag.Gateway.Infrastructure/", "Simcag.Gateway.Infrastructure/"]

RUN dotnet publish "Simcag.Gateway.Api/Simcag.Gateway.Api.csproj" \
    --configuration "${BUILD_CONFIGURATION}" \
    --output /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

WORKDIR /app

RUN groupadd -r appgroup && useradd -r -g appgroup appuser

COPY --from=builder /app/publish .

USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && chown -R appuser:appgroup /app

ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    ASPNETCORE_URLS=http://+:8080

EXPOSE 8080

USER appuser

HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
    CMD curl -fsS http://localhost:8080/health >/dev/null || exit 1

ENTRYPOINT ["dotnet", "Simcag.Gateway.Api.dll"]
