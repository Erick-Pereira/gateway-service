FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS builder
WORKDIR /src

ARG BUILD_CONFIGURATION=Release

COPY ["Simcag.Gateway.Api/Simcag.Gateway.Api.csproj", "Simcag.Gateway.Api/"]
COPY ["Simcag.Gateway.Application/Simcag.Gateway.Application.csproj", "Simcag.Gateway.Application/"]
COPY ["Simcag.Gateway.Domain/Simcag.Gateway.Domain.csproj", "Simcag.Gateway.Domain/"]
COPY ["Simcag.Gateway.Infrastructure/Simcag.Gateway.Infrastructure.csproj", "Simcag.Gateway.Infrastructure/"]

RUN dotnet restore "Simcag.Gateway.Api/Simcag.Gateway.Api.csproj"

COPY . .

RUN dotnet publish "Simcag.Gateway.Api/Simcag.Gateway.Api.csproj" \
    --configuration "${BUILD_CONFIGURATION}" \
    --output /app/publish \
    --no-restore \
    /p:UseAppHost=false


FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime
WORKDIR /app

RUN groupadd -r appgroup && useradd -r -g appgroup appuser

COPY --from=builder /app/publish .

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && chown -R appuser:appgroup /app

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true

EXPOSE 8080

USER appuser

HEALTHCHECK --interval=30s --timeout=8s --start-period=25s --retries=3 \
    CMD sh -c 'p="${ASPNETCORE_HTTP_PORTS:-8080}"; p="${p%%,*}"; curl -fsS "http://127.0.0.1:${p}/health/live" >/dev/null || exit 1'

ENTRYPOINT ["dotnet", "Simcag.Gateway.Api.dll"]