FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG NUGET_GITHUB_USERNAME
ARG NUGET_GITHUB_TOKEN
WORKDIR /src

COPY ["Directory.Packages.props", "."]
COPY ["nuget.config", "."]

COPY ["TripRadar.Server.Db/TripRadar.Server.Db.csproj", "TripRadar.Server.Db/"]
COPY ["TripRadar.Server.Domain/TripRadar.Server.Domain.csproj", "TripRadar.Server.Domain/"]

# Configure GitHub Packages source with credentials (package source mapping in nuget.config ensures only TripRadar.* packages use this)
# Remove existing source first to avoid duplicate name error
RUN dotnet nuget remove source github 2>/dev/null || true
RUN dotnet nuget add source "https://nuget.pkg.github.com/RoseXTechnology/index.json" \
    --name github \
    --username "${NUGET_GITHUB_USERNAME}" \
    --password "${NUGET_GITHUB_TOKEN}" \
    --store-password-in-clear-text

RUN dotnet restore "TripRadar.Server.Db/TripRadar.Server.Db.csproj"

COPY . .
WORKDIR "/src/TripRadar.Server.Db"
RUN dotnet build "TripRadar.Server.Db.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "TripRadar.Server.Db.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .
COPY TripRadar.Server.Db/Data /app/Data

# Create non-root user and switch to it
RUN adduser --disabled-password --gecos "" --uid 1000 appuser && \
    chown -R appuser:appuser /app
USER appuser

ENTRYPOINT ["dotnet", "TripRadar.Server.Db.dll"]

