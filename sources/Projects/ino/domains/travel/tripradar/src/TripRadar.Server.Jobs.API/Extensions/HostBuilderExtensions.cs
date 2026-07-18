namespace TripRadar.Server.Jobs.API.Extensions;

public static class HostBuilderExtensions
{
    public static void ConfigureHostBuilder(this IHostBuilder hostBuilder, IHostEnvironment environment)
    {
        // Logging is configured by AddServiceDefaults() (OpenTelemetry + OTLP export).
        // Do not call ClearProviders() here — it would wipe the OTel log provider.
    }
}
