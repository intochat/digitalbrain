using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace DigitalBrain.Client;

internal static class ClientHostingDefaults
{
    public static void AddDigitalBrainClientDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });
        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter("DigitalBrain")
                .AddMeter("Microsoft.Orleans"))
            .WithTracing(tracing => tracing
                .AddSource(builder.Environment.ApplicationName)
                .AddSource("DigitalBrain")
                .AddSource("Microsoft.Orleans.Application")
                .AddAspNetCoreInstrumentation(options =>
                    options.Filter = context =>
                        !context.Request.Path.StartsWithSegments("/health")
                        && !context.Request.Path.StartsWithSegments("/alive"))
                .AddHttpClientInstrumentation());
        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        builder.Services.AddHealthChecks()
            .AddCheck("self", static () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(), ["live"]);
        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http => http.AddServiceDiscovery());
    }
}
