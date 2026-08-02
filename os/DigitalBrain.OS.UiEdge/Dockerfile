# syntax=docker/dockerfile:1
#
# Product image: digitalbrain-ui
#   - OS.UiEdge HTTP/SSE edge: chat, OAuth MCP callback, user actions, behavior APIs, static web
#
# Build (repo root):
#   docker build -f os/DigitalBrain.OS.UiEdge/Dockerfile -t digitalbrain-ui .
#
# No secrets are baked into this image. Inject at run/deploy (Azure Key Vault → env):
#
#   Owner / client
#     DigitalBrain__Owner
#
#   Orleans client clustering (same table account as silo)
#     ConnectionStrings__brain-clustering
#
#   Module topology for client (same module ids as silo, no silo-only secrets)
#     DigitalBrain__Modules__0=DigitalBrain.AI.AIModule
#     … (see digitalbrain image header for full product list)
#
#   OAuth callbacks land on this edge at /oauth/callback — register that URI on
#   Google/Salesforce apps. Provider client id/secret live on the silo (digitalbrain),
#   not on this image.
#
# Port: 8080 HTTP (ASP.NET container default)

FROM mcr.microsoft.com/dotnet/sdk:11.0-preview AS build
WORKDIR /src

COPY nuget.config Directory.Build.props Directory.Packages.props DigitalBrain.slnx global.json ./
COPY src/ ./src/
COPY os/ ./os/
COPY samples/ ./samples/
COPY assets/ ./assets/

RUN dotnet publish os/DigitalBrain.OS.UiEdge/DigitalBrain.OS.UiEdge.csproj \
      -c Release \
      -o /out/ui \
      --nologo

FROM mcr.microsoft.com/dotnet/aspnet:11.0-preview AS final
WORKDIR /app

USER $APP_UID

COPY --from=build --chown=$APP_UID:$APP_UID /out/ui /app

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://0.0.0.0:8080 \
    DOTNET_EnableDiagnostics=0

EXPOSE 8080

ENTRYPOINT ["dotnet", "DigitalBrain.OS.UiEdge.dll"]
