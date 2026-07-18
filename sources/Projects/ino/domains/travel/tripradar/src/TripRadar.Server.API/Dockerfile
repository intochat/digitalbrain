FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG NUGET_GITHUB_USERNAME
ARG NUGET_GITHUB_TOKEN
WORKDIR /src

COPY ["Directory.Packages.props", "."]
COPY ["nuget.config", "."]

COPY ["TripRadar.Server.API/TripRadar.Server.API.csproj", "TripRadar.Server.API/"]
COPY ["TripRadar.Server.API.Contracts/TripRadar.Server.API.Contracts.csproj", "TripRadar.Server.API.Contracts/"]
COPY ["TripRadar.Server.Application/TripRadar.Server.Application.csproj", "TripRadar.Server.Application/"]
COPY ["TripRadar.Server.Domain/TripRadar.Server.Domain.csproj", "TripRadar.Server.Domain/"]
COPY ["TripRadar.Server.Infrastructure/TripRadar.Server.Infrastructure.csproj", "TripRadar.Server.Infrastructure/"]
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

RUN dotnet restore "TripRadar.Server.API/TripRadar.Server.API.csproj"

COPY . .

WORKDIR "/src/TripRadar.Server.API"
RUN dotnet build "TripRadar.Server.API.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "TripRadar.Server.API.csproj" -c Release -o /app/publish /p:UseAppHost=false -p:ExcludeConfigFilesFromBuildOutput=true

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
COPY --from=build /src/TripRadar.Server.API/appsettings*.json ./

# Create non-root user and switch to it
RUN adduser --disabled-password --gecos "" --uid 1000 appuser && \
    chown -R appuser:appuser /app
USER appuser

ENTRYPOINT ["dotnet", "TripRadar.Server.API.dll"]

