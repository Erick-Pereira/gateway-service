# Imagem a partir do diretório gateway-service/ (ver também docker-compose.yml).
# No monorepô completo, prefira: docker build -f gateway-service/Simcag.Gateway.Api/Dockerfile -t simcag/gateway (contexto: raiz do repositório).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Simcag.Gateway.Api/Simcag.Gateway.Api.csproj", "Simcag.Gateway.Api/"]
COPY ["Simcag.Gateway.Application/Simcag.Gateway.Application.csproj", "Simcag.Gateway.Application/"]
COPY ["Simcag.Gateway.Domain/Simcag.Gateway.Domain.csproj", "Simcag.Gateway.Domain/"]
COPY ["Simcag.Gateway.Infrastructure/Simcag.Gateway.Infrastructure.csproj", "Simcag.Gateway.Infrastructure/"]
RUN dotnet restore "Simcag.Gateway.Api/Simcag.Gateway.Api.csproj"
COPY Simcag.Gateway.Api/ Simcag.Gateway.Api/
COPY Simcag.Gateway.Application/ Simcag.Gateway.Application/
COPY Simcag.Gateway.Domain/ Simcag.Gateway.Domain/
COPY Simcag.Gateway.Infrastructure/ Simcag.Gateway.Infrastructure/
RUN dotnet publish "Simcag.Gateway.Api/Simcag.Gateway.Api.csproj" -c ${BUILD_CONFIGURATION} -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_RUNNING_IN_CONTAINER=true
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Simcag.Gateway.Api.dll"]
