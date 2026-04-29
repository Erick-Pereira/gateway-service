# Build context: raiz do repositório (sem Simcag.Shared)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["gateway-service/Simcag.Gateway.Api/Simcag.Gateway.Api.csproj", "gateway-service/Simcag.Gateway.Api/"]
COPY ["gateway-service/Simcag.Gateway.Application/Simcag.Gateway.Application.csproj", "gateway-service/Simcag.Gateway.Application/"]
COPY ["gateway-service/Simcag.Gateway.Domain/Simcag.Gateway.Domain.csproj", "gateway-service/Simcag.Gateway.Domain/"]
COPY ["gateway-service/Simcag.Gateway.Infrastructure/Simcag.Gateway.Infrastructure.csproj", "gateway-service/Simcag.Gateway.Infrastructure/"]
RUN dotnet restore "gateway-service/Simcag.Gateway.Api/Simcag.Gateway.Api.csproj"
COPY gateway-service/ gateway-service/
RUN dotnet publish "gateway-service/Simcag.Gateway.Api/Simcag.Gateway.Api.csproj" -c ${BUILD_CONFIGURATION} -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Simcag.Gateway.Api.dll"]
