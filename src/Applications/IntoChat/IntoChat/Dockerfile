# syntax=docker/dockerfile:1
#
# Hosted product kernel image.
# Production pushes use SDK PublishContainer from .github/workflows/deploy.yml (single
# digitalbrain-kernel image). This Dockerfile is the local/smoke equivalent and must stay in
# step with the product AppHost profile.
#
# Build (repo root):
#   docker build -f src/Applications/IntoChat/IntoChat/Dockerfile -t vhorbachov/digitalbrain-kernel:local .
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
# The product profile excludes the developer executors: Coding, CSharp, Roslyn, DotNet
# and Aspire are not in the module list below.
#
# Ports: 8080 silo HTTP (broker + health).

FROM mcr.microsoft.com/dotnet/sdk:11.0.100-rc.1 AS build
WORKDIR /src

COPY nuget.config Directory.Build.props Directory.Packages.props DigitalBrain.slnx global.json ./
COPY src/ ./src/

RUN dotnet publish src/Applications/IntoChat/IntoChat/IntoChat.csproj \
      -c Release \
      -r linux-x64 \
      --self-contained false \
      -o /out/silo \
      --nologo

FROM mcr.microsoft.com/dotnet/aspnet:11.0.0-rc.1 AS final
WORKDIR /app

COPY --from=build /out/silo /app/silo

# Product module set matching the product AppHost profile (no developer modules).
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://0.0.0.0:8080 \
    DOTNET_EnableDiagnostics=0 \
    IntoChat__Hosted__Enabled=true \
    DigitalBrain__Standalone=true \
    DigitalBrain__Modules__0="DigitalBrain.AI.AIModule, DigitalBrain.Modules.AI" \
    DigitalBrain__Modules__1="DigitalBrain.Memory.MemoryModule, DigitalBrain.Modules.Memory" \
    DigitalBrain__Modules__2="DigitalBrain.ClickHouse.ClickHouseModule, DigitalBrain.Modules.ClickHouse" \
    DigitalBrain__Modules__3="DigitalBrain.Supabase.SupabaseModule, DigitalBrain.Modules.Supabase" \
    DigitalBrain__Modules__4="DigitalBrain.Time.TimeModule, DigitalBrain.Modules.Time" \
    DigitalBrain__Modules__5="DigitalBrain.Platform.Secrets.SecretsModule, DigitalBrain.Platform" \
    DigitalBrain__Modules__6="DigitalBrain.Platform.Integrations.IntegrationsModule, DigitalBrain.Platform" \
    DigitalBrain__Modules__7="DigitalBrain.Identity.IdentityModule, DigitalBrain.Modules.Identity" \
    DigitalBrain__Modules__8="DigitalBrain.Google.Gmail.GmailModule, DigitalBrain.Modules.Google.Gmail" \
    DigitalBrain__Modules__9="DigitalBrain.Salesforce.SalesforceModule, DigitalBrain.Modules.Salesforce" \
    DigitalBrain__Modules__10="DigitalBrain.Microsoft.GitHub.GitHubModule, DigitalBrain.Modules.Microsoft.GitHub" \
    DigitalBrain__Modules__11="DigitalBrain.Flutter.FlutterModule, DigitalBrain.Modules.Flutter" \
    DigitalBrain__Modules__12="DigitalBrain.Compute.ComputeModule, DigitalBrain.Modules.Compute" \
    DigitalBrain__Modules__13="DigitalBrain.Registry.RegistryModule, DigitalBrain.Modules.Registry" \
    DigitalBrain__Modules__14="DigitalBrain.Apps.AppsModule, DigitalBrain.Modules.Apps" \
    DigitalBrain__Modules__15="DigitalBrain.Qdrant.QdrantModule, DigitalBrain.Modules.Qdrant" \
    DigitalBrain__Modules__16="DigitalBrain.Files.FilesModule, DigitalBrain.Modules.Files"

EXPOSE 8080

USER $APP_UID
ENTRYPOINT ["dotnet", "/app/silo/IntoChat.dll"]
