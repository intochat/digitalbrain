FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443
EXPOSE 5201

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
ARG NUGET_GITHUB_USERNAME
ARG NUGET_GITHUB_TOKEN
WORKDIR /src

COPY ["Directory.Packages.props", "."]
COPY ["nuget.config", "."]

COPY ["TripRadar.Server.Jobs.API/TripRadar.Server.Jobs.API.csproj", "TripRadar.Server.Jobs.API/"]
COPY ["TripRadar.Server.Infrastructure/TripRadar.Server.Infrastructure.csproj", "TripRadar.Server.Infrastructure/"]
COPY ["TripRadar.Server.Application/TripRadar.Server.Application.csproj", "TripRadar.Server.Application/"]
COPY ["TripRadar.Server.Domain/TripRadar.Server.Domain.csproj", "TripRadar.Server.Domain/"]
COPY ["TripRadar.Server.Db/TripRadar.Server.Db.csproj", "TripRadar.Server.Db/"]
COPY ["TripRadar.Server.Mocks/TripRadar.Server.Mocks.csproj", "TripRadar.Server.Mocks/"]

# Configure GitHub Packages source with credentials (package source mapping in nuget.config ensures only TripRadar.* packages use this)
# Remove existing source first to avoid duplicate name error
RUN dotnet nuget remove source github 2>/dev/null || true
RUN dotnet nuget add source "https://nuget.pkg.github.com/RoseXTechnology/index.json" \
    --name github \
    --username "${NUGET_GITHUB_USERNAME}" \
    --password "${NUGET_GITHUB_TOKEN}" \
    --store-password-in-clear-text

RUN dotnet restore "TripRadar.Server.Jobs.API/TripRadar.Server.Jobs.API.csproj"

COPY . .

WORKDIR "/src/TripRadar.Server.Jobs.API"
RUN dotnet build "TripRadar.Server.Jobs.API.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "TripRadar.Server.Jobs.API.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Create non-root user and switch to it
RUN adduser --disabled-password --gecos "" --uid 1000 appuser && \
    chown -R appuser:appuser /app
USER appuser

ENTRYPOINT ["dotnet", "TripRadar.Server.Jobs.API.dll"]

