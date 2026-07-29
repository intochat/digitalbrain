using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace DigitalBrain.ServiceDefaults;

public static class ServiceDefaultsExtensions
{
    private const string AlivePath = "/alive";
    private const string HealthPath = "/health";
    private const string TelemetryPrefix = "DigitalBrain";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        ConfigureOpenTelemetry(builder);
        AddDefaultHealthChecks(builder);

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapHealthChecks(HealthPath);
        app.MapHealthChecks(AlivePath, new HealthCheckOptions
        {
            Predicate = static registration => registration.Tags.Contains("live"),
        });

        return app;
    }

    private static void AddDefaultHealthChecks<TBuilder>(TBuilder builder)
        where TBuilder : IHostApplicationBuilder
        => builder.Services
            .AddHealthChecks()
            .AddCheck("self", static () => HealthCheckResult.Healthy(), ["live"]);

    private static void AddOpenTelemetryExporters<TBuilder>(TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }
    }

    private static void ConfigureOpenTelemetry<TBuilder>(TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        var configuredSampleRatio = builder.Configuration.GetValue<double?>("Telemetry:Tracing:SampleRatio");
        var sampleRatio = Math.Clamp(configuredSampleRatio ?? 1d, 0d, 1d);

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                serviceName: builder.Environment.ApplicationName,
                serviceNamespace: TelemetryPrefix))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter(TelemetryPrefix)
                .AddMeter($"{TelemetryPrefix}.*")
                .AddMeter("Experimental.Microsoft.Extensions.AI")
                .AddMeter("Microsoft.Orleans"))
            .WithTracing(tracing => tracing
                .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(sampleRatio)))
                .AddSource(builder.Environment.ApplicationName)
                .AddSource(TelemetryPrefix)
                .AddSource($"{TelemetryPrefix}.*")
                .AddSource("Experimental.Microsoft.Extensions.AI")
                .AddSource("Microsoft.Orleans.Application")
                .AddAspNetCoreInstrumentation(options =>
                    options.Filter = context =>
                        !context.Request.Path.StartsWithSegments(HealthPath, StringComparison.OrdinalIgnoreCase)
                        && !context.Request.Path.StartsWithSegments(AlivePath, StringComparison.OrdinalIgnoreCase))
                .AddHttpClientInstrumentation());

        AddOpenTelemetryExporters(builder);
    }
}
