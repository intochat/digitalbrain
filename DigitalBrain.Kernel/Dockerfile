# syntax=docker/dockerfile:1
#
# Product image: digitalbrain
#   - Orleans silo (DigitalBrain.Kernel) — brokers + health on 8080
#   - Northbound MCP (DigitalBrain.Mcp) as cluster client at /mcp on port 5000
#   - AppHost remains separate for local aspire run; Docker does not include AppHost
#
# Build (repo root):
#   docker build -f DigitalBrain.Kernel/Dockerfile -t digitalbrain .
#
# No secrets are baked into this image. Inject at run/deploy (Azure Key Vault → env):
#
#   Google Gmail OAuth (config root DigitalBrain:Google:Gmail)
#     DigitalBrain__Google__Gmail__ClientId
#     DigitalBrain__Google__Gmail__ClientSecret
#     DigitalBrain__Google__Gmail__RedirectUri     # UI base + /oauth/callback
#
#   Salesforce OAuth (config root DigitalBrain:Salesforce; no client secret required)
#     DigitalBrain__Salesforce__ClientId
#     DigitalBrain__Salesforce__RedirectUri        # UI base + /oauth/callback
#
#   Models (L2)
#     DigitalBrain__AI__Ollama__Endpoint           # e.g. http://host.docker.internal:11434
#     DigitalBrain__AI__Ollama__Gemma4__Model      # optional model tag overrides
#     DigitalBrain__AI__Ollama__Llama32__Model
#     DigitalBrain__AI__OpenAI__Endpoint           # Azure OpenAI / OpenAI-compatible
#     DigitalBrain__AI__OpenAI__ApiKey
#     DigitalBrain__AI__OpenAI__Gpt56__Model
#
#   Durable state protection
#     DigitalBrain__Security__StateProtectionKey
#
#   Azure Storage (Orleans clustering, reminders, journal)
#     ConnectionStrings__brain-clustering
#     ConnectionStrings__brain-reminders
#     ConnectionStrings__journal
#
#   Qdrant vector memory (platform dependency — not this image)
#     DigitalBrain__Memory__Provider=Qdrant
#     DigitalBrain__Memory__Qdrant__ConnectionName=memory-qdrant
#     ConnectionStrings__memory-qdrant             # Endpoint=http://qdrant:6334;Key=...
#
#   Module manifest (must match silo catalog; AppHost order for product)
#     DigitalBrain__Modules__0=DigitalBrain.AI.AIModule
#     DigitalBrain__Modules__1=DigitalBrain.Chat.ChatModule
#     DigitalBrain__Modules__2=DigitalBrain.Memory.MemoryModule
#     DigitalBrain__Modules__3=DigitalBrain.Assistant.AssistantModule
#     DigitalBrain__Modules__4=DigitalBrain.Shell.ShellModule
#     DigitalBrain__Modules__5=DigitalBrain.Google.GoogleModule
#     DigitalBrain__Modules__6=DigitalBrain.Salesforce.SalesforceModule
#     DigitalBrain__Modules__7=DigitalBrain.Behaviors.Runtime.BehaviorsModule
#     DigitalBrain__Modules__8=DigitalBrain.Tasks.TasksModule
#     DigitalBrain__Modules__9=DigitalBrain.Time.TimeModule
#     DigitalBrain__Modules__10=DigitalBrain.Introspection.IntrospectionModule
#
# Ports: 8080 silo HTTP (broker + health), 5000 northbound MCP (/mcp)

FROM mcr.microsoft.com/dotnet/sdk:11.0-preview AS build
WORKDIR /src

COPY nuget.config Directory.Build.props Directory.Packages.props DigitalBrain.slnx global.json ./
COPY src/ ./src/
COPY product/ ./product/
COPY DigitalBrain.Kernel/ ./DigitalBrain.Kernel/
COPY DigitalBrain.Mcp/ ./DigitalBrain.Mcp/
COPY samples/ ./samples/
COPY assets/ ./assets/

RUN dotnet publish DigitalBrain.Kernel/DigitalBrain.Kernel.csproj \
      -c Release \
      -o /out/silo \
      --nologo \
 && dotnet publish DigitalBrain.Mcp/DigitalBrain.Mcp.csproj \
      -c Release \
      -o /out/mcp \
      --nologo

FROM mcr.microsoft.com/dotnet/aspnet:11.0-preview AS final
WORKDIR /app

COPY --from=build /out/silo /app/silo
COPY --from=build /out/mcp /app/mcp
COPY --chmod=755 DigitalBrain.Kernel/docker-entrypoint.sh /app/docker-entrypoint.sh

ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_EnableDiagnostics=0

EXPOSE 8080 5000

USER $APP_UID
ENTRYPOINT ["/app/docker-entrypoint.sh"]
