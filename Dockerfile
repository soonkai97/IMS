# API image. Build context = repository root.
#   docker build -t ims-api .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Ims.sln ./
COPY src/Ims.Domain/Ims.Domain.csproj src/Ims.Domain/
COPY src/Ims.Application/Ims.Application.csproj src/Ims.Application/
COPY src/Ims.Infrastructure/Ims.Infrastructure.csproj src/Ims.Infrastructure/
COPY src/Ims.Api/Ims.Api.csproj src/Ims.Api/
RUN dotnet restore src/Ims.Api/Ims.Api.csproj
COPY src/ src/
RUN dotnet publish src/Ims.Api/Ims.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
# Non-root user. Writable folders owned by that user: logs, the physical file storage (mount a volume on /app/storage) and the
# key ring that encrypts list cursors (mount a volume on /app/keys: it must survive redeploys and be shared by every instance).
RUN mkdir -p /app/logs /app/storage /app/keys && chown -R $APP_UID /app/logs /app/storage /app/keys
USER $APP_UID
ENV ASPNETCORE_URLS=http://+:8080 \
    Logging__Directory=/app/logs \
    Storage__RootPath=/app/storage \
    DataProtection__KeysPath=/app/keys
EXPOSE 8080
# Health is served at GET /health (database check). The runtime image has no curl, so probes belong to the orchestrator
# (docker compose healthcheck, Kubernetes httpGet, load balancer) rather than a HEALTHCHECK instruction here.
ENTRYPOINT ["dotnet", "Ims.Api.dll"]
