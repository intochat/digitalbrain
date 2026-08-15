using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
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
    private const string HealthPath = "/health";
    private const string AlivePath = "/alive";
    private const string ServiceNamespace = "DigitalBrain";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        builder.Services
            .AddHealthChecks()
            .AddCheck("self", static () => HealthCheckResult.Healthy(), ["live"]);

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services
            .AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                serviceName: builder.Environment.ApplicationName,
                serviceNamespace: ServiceNamespace))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter(ServiceNamespace)
                .AddMeter("Microsoft.Orleans"))
            .WithTracing(tracing => tracing
                .AddSource(builder.Environment.ApplicationName)
                .AddSource(ServiceNamespace)
                .AddSource("Microsoft.Orleans.Application")
                .AddAspNetCoreInstrumentation(options =>
                    options.Filter = context =>
                        !context.Request.Path.StartsWithSegments(HealthPath, StringComparison.OrdinalIgnoreCase)
                        && !context.Request.Path.StartsWithSegments(AlivePath, StringComparison.OrdinalIgnoreCase))
                .AddHttpClientInstrumentation());

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapHealthChecks(HealthPath).AllowAnonymous();
        app.MapHealthChecks(AlivePath, new HealthCheckOptions
        {
            Predicate = static registration => registration.Tags.Contains("live"),
        }).AllowAnonymous();

        return app;
    }
}
