FROM mcr.microsoft.com/dotnet/aspnet:11.0
USER root
RUN useradd --uid 10001 --no-log-init --create-home --shell /usr/sbin/nologin sandbox
USER sandbox
WORKDIR /behavior
ENV DOTNET_EnableDiagnostics=0 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    HOME=/tmp
ENTRYPOINT ["dotnet"]
