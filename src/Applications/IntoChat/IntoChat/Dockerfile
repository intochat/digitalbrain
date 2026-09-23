# syntax=docker/dockerfile:1
#
# Local/smoke kernel image.
# Production Hub pushes use SDK PublishContainer from .github/workflows/deploy.yml
# (single digitalbrain-kernel image).
#
# Build (repo root):
#   docker build -f src/Applications/IntoChat/IntoChat/Dockerfile -t vhorbachov/digitalbrain-kernel:local .
#
# No secrets are baked into this image. Inject at run/deploy (Azure Key Vault → env):
#
#   Models (cloud prod — prefer API keys; Ollama is local/dev)
#     DigitalBrain__AI__OpenAI__ApiKey
#     DigitalBrain__AI__Anthropic__ApiKey
#     DigitalBrain__AI__Google__ApiKey
#     DigitalBrain__AI__XAI__ApiKey
#     DigitalBrain__AI__Default__Model
#
#   Azure Storage (Orleans clustering, reminders, grain state)
#     ConnectionStrings__clustering
#     ConnectionStrings__reminders
#     ConnectionStrings__grainstate
#
#   Qdrant vector memory (platform dependency — not this image)
#     DigitalBrain__Memory__Provider=Qdrant
#     DigitalBrain__Memory__Qdrant__ConnectionName=memory-qdrant
#     ConnectionStrings__memory-qdrant             # Endpoint=http://qdrant:6334;Key=...
#
#   Module manifest defaults are set below; override DigitalBrain__Modules__N if needed.
#
# Ports: 8080 silo HTTP (broker + health).

FROM mcr.microsoft.com/dotnet/sdk:11.0-preview AS build
WORKDIR /src

COPY nuget.config Directory.Build.props Directory.Packages.props DigitalBrain.slnx global.json ./
COPY src/ ./src/

RUN dotnet publish src/Applications/IntoChat/IntoChat/IntoChat.csproj \
      -c Release \
      -r linux-x64 \
      --self-contained false \
      -o /out/silo \
      --nologo

FROM mcr.microsoft.com/dotnet/aspnet:11.0-preview AS final
WORKDIR /app

COPY --from=build /out/silo /app/silo
COPY --chmod=755 src/Applications/IntoChat/IntoChat/docker-entrypoint.sh /app/docker-entrypoint.sh

# Product module set matching the live AppHost module chain.
ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_EnableDiagnostics=0 \
    DigitalBrain__Modules__0="DigitalBrain.AI.AIModule, DigitalBrain.Modules.AI" \
    DigitalBrain__Modules__1="DigitalBrain.Memory.MemoryModule, DigitalBrain.Modules.Memory" \
    DigitalBrain__Modules__2="DigitalBrain.ClickHouse.ClickHouseModule, DigitalBrain.Modules.ClickHouse" \
    DigitalBrain__Modules__3="DigitalBrain.Supabase.SupabaseModule, DigitalBrain.Modules.Supabase" \
    DigitalBrain__Modules__4="DigitalBrain.Time.TimeModule, DigitalBrain.Modules.Time" \
    DigitalBrain__Modules__5="DigitalBrain.Google.Gmail.GmailModule, DigitalBrain.Modules.Google.Gmail" \
    DigitalBrain__Modules__6="DigitalBrain.Salesforce.SalesforceModule, DigitalBrain.Modules.Salesforce" \
    DigitalBrain__Modules__7="DigitalBrain.Microsoft.Aspire.AspireModule, DigitalBrain.Modules.Microsoft.Aspire" \
    DigitalBrain__Modules__8="DigitalBrain.Microsoft.GitHub.GitHubModule, DigitalBrain.Modules.Microsoft.GitHub" \
    DigitalBrain__Modules__9="DigitalBrain.Microsoft.Roslyn.RoslynModule, DigitalBrain.Modules.Microsoft.Roslyn" \
    DigitalBrain__Modules__10="DigitalBrain.Microsoft.DotNet.DotNetModule, DigitalBrain.Modules.Microsoft.DotNet" \
    DigitalBrain__Modules__11="DigitalBrain.Coding.CodingModule, DigitalBrain.Modules.Coding" \
    DigitalBrain__Modules__12="DigitalBrain.Behavior.BehaviorModule, DigitalBrain.Modules.Behavior" \
    DigitalBrain__Modules__13="DigitalBrain.Flutter.FlutterModule, DigitalBrain.Modules.Flutter"

EXPOSE 8080

USER $APP_UID
ENTRYPOINT ["/app/docker-entrypoint.sh"]
