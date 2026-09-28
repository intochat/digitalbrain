# Scripts compile inside the sandbox, so the runtime stage keeps the full SDK.
FROM mcr.microsoft.com/dotnet/sdk:11.0.100-rc.1 AS build
WORKDIR /src
COPY . .
RUN dotnet publish DigitalBrain.Modules.Microsoft.CSharp.Sandbox.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/sdk:11.0.100-rc.1
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1
EXPOSE 8080
ENTRYPOINT ["dotnet", "DigitalBrain.Modules.Microsoft.CSharp.Sandbox.dll"]
