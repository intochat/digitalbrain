# syntax=docker/dockerfile:1
#
# Hosted product kernel image.
# Production pushes use SDK PublishContainer from .github/workflows/deploy.yml (single
# digitalbrain-kernel image). This Dockerfile is the local/smoke equivalent and must stay in
# step with the product AppHost profile.
#
# Build (repo root):
#   docker build -f src/IntoChat/IntoChat/Dockerfile -t vhorbachov/digitalbrain-kernel:local .
#
# No secrets are baked into this image. Managed identity + Key Vault are injected at run/deploy:
#
#   AZURE_CLIENT_ID                 user-assigned managed identity for the container app
#   DigitalBrain__KeyVault__Uri     Key Vault URI resolved by the managed identity (config only)
#   ConnectionStrings__clustering   Orleans clustering (Azure Table/Blob)
#   ConnectionStrings__reminders    Orleans reminders
#   ConnectionStrings__grainstate   Orleans grain state (digitalbrain-v2-state)
#   ConnectionStrings__qdrant  Endpoint=http://qdrant:6334;Key=...
#
# Ports: 8080 silo HTTP (broker + health).

FROM mcr.microsoft.com/dotnet/sdk:11.0.100-rc.1 AS build
WORKDIR /src

COPY nuget.config Directory.Build.props Directory.Packages.props DigitalBrain.slnx global.json ./
COPY src/ ./src/

RUN dotnet publish src/IntoChat/IntoChat/IntoChat.csproj \
      -c Release \
      -r linux-x64 \
      --self-contained false \
      -o /out/silo \
      --nologo

FROM mcr.microsoft.com/dotnet/aspnet:11.0.0-rc.1 AS final
WORKDIR /app

COPY --from=build /out/silo /app/silo

# Product module set checked against AppHost composition by the host unit suite.
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://0.0.0.0:8080 \
    DOTNET_EnableDiagnostics=0 \
    IntoChat__Hosted__Enabled=true \
    DigitalBrain__Standalone=true \
    DigitalBrain__Modules__ai__Enabled="true" \
    DigitalBrain__Modules__clickhouse__Enabled="true" \
    DigitalBrain__Modules__supabase__Enabled="true" \
    DigitalBrain__Modules__time__Enabled="true" \
    DigitalBrain__Modules__gmail__Enabled="true" \
    DigitalBrain__Modules__salesforce__Enabled="true" \
    DigitalBrain__Modules__github__Enabled="true" \
    DigitalBrain__Modules__flutter__Enabled="true" \
    DigitalBrain__Modules__compute__Enabled="true" \
    DigitalBrain__Modules__registry__Enabled="true" \
    DigitalBrain__Modules__apps__Enabled="true" \
    DigitalBrain__Modules__qdrant__Enabled="true" \
    DigitalBrain__Modules__files__Enabled="true" \
    DigitalBrain__Modules__postgres__Enabled="true" \
    DigitalBrain__Modules__assistant__Enabled="true" \
    DigitalBrain__Modules__playwright__Enabled="true" \
    DigitalBrain__Modules__specs__Enabled="true" \
    DigitalBrain__Modules__aspire__Enabled="true" \
    DigitalBrain__Modules__csharp__Enabled="true" \
    DigitalBrain__Modules__csharp-authoring__Enabled="true"

EXPOSE 8080

USER $APP_UID
ENTRYPOINT ["dotnet", "/app/silo/IntoChat.dll"]
