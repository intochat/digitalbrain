# syntax=docker/dockerfile:1
#
# Product image: digitalbrain
#   - Orleans silo (DigitalBrain.Kernel) — brokers + health on 8080
#   - Northbound MCP (DigitalBrain.Mcp) as cluster client at /mcp on port 5000
#   - AppHost remains separate for local aspire run; Docker does not include AppHost
#
# Build (repo root):
#   docker build -f src/Kernel/DigitalBrain.Kernel/Dockerfile -t digitalbrain .
#
# No secrets are baked into this image. Inject at run/deploy (Azure Key Vault → env):
#
#   Models selected by AppHost
#     DigitalBrain__AI__Ollama__Endpoint           # e.g. http://host.docker.internal:11434
#     DigitalBrain__AI__Ollama__IGemma4__Model
#     DigitalBrain__AI__Ollama__IEmbeddingGemma__Model
#     DigitalBrain__AI__Whisper__ModelId
#
#   Azure Storage (Orleans clustering, reminders, grain state, journal)
#     ConnectionStrings__clustering
#     ConnectionStrings__reminders
#     ConnectionStrings__grainstate
#     ConnectionStrings__journal
#
#   Qdrant vector memory (platform dependency — not this image)
#     DigitalBrain__Memory__Provider=Qdrant
#     DigitalBrain__Memory__Qdrant__ConnectionName=memory-qdrant
#     ConnectionStrings__memory-qdrant             # Endpoint=http://qdrant:6334;Key=...
#
#   AppHost writes the explicit module manifest through
#   DigitalBrain__Modules__N. The runtime loads only those module types.
#
# Ports: 8080 silo HTTP (broker + health), 5000 northbound MCP (/mcp)

FROM mcr.microsoft.com/dotnet/sdk:11.0-preview AS build
WORKDIR /src

COPY nuget.config Directory.Build.props Directory.Packages.props DigitalBrain.slnx global.json ./
COPY src/ ./src/

RUN dotnet publish src/Kernel/DigitalBrain.Kernel/DigitalBrain.Kernel.csproj \
      -c Release \
      -o /out/silo \
      --nologo \
 && dotnet publish src/Kernel/DigitalBrain.Mcp/DigitalBrain.Mcp.csproj \
      -c Release \
      -o /out/mcp \
      --nologo

FROM mcr.microsoft.com/dotnet/aspnet:11.0-preview AS final
WORKDIR /app

COPY --from=build /out/silo /app/silo
COPY --from=build /out/mcp /app/mcp
COPY --chmod=755 src/Kernel/DigitalBrain.Kernel/docker-entrypoint.sh /app/docker-entrypoint.sh

ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_EnableDiagnostics=0

EXPOSE 8080 5000

USER $APP_UID
ENTRYPOINT ["/app/docker-entrypoint.sh"]
