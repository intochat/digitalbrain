using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace DigitalBrain.Runtime;

public static class DigitalBrainServiceDefaults
{
    const string HealthEndpointPath = "/health";
    const string AlivenessEndpointPath = "/alive";

    public static TBuilder AddDigitalBrainDomain<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.AddServiceDefaults();
        builder.AddKeyedRedisClient("orleans-redis");
        return builder;
    }

    public static TBuilder AddDigitalBrainClient<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.AddServiceDefaults();
        return builder;
    }

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.ApplyDigitalBrainLogDefaults();
        builder.Services.AddHostedService<ResourceReadyLogger>();
        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();
        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });
        return builder;
    }

    // Centralized log-noise floor for every silo, the kernel, and clients.
    // The framework chatter (Orleans/Redis/gRPC/HttpClient/AspNetCore/Aspire)
    // is the source of the unreadable startup stream; we floor it to Warning
    // here so a fresh `aspire start` is one intentional line per resource
    // (see ResourceReadyLogger) plus genuine warnings/errors only.
    //
    // These are DEFAULTS, not overrides: the in-memory source is inserted at
    // index 0 (lowest precedence) so any appsettings.json / environment value
    // a human supplies wins per-key — e.g. set Logging:LogLevel:Orleans=Debug
    // ad-hoc without editing this file. Category rules carry no provider
    // segment, so they filter the console AND the OpenTelemetry logging
    // provider (the Aspire dashboard's structured-log stream) identically.
    static TBuilder ApplyDigitalBrainLogDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        var defaults = new Dictionary<string, string?>
        {
            // Keep our own DigitalBrain.* (and anything not floored below) readable.
            ["Logging:LogLevel:Default"] = "Information",

            ["Logging:LogLevel:Microsoft"] = "Warning",
            ["Logging:LogLevel:Microsoft.Hosting.Lifetime"] = "Warning",
            ["Logging:LogLevel:Microsoft.AspNetCore"] = "Warning",
            ["Logging:LogLevel:Microsoft.Orleans"] = "Warning",
            ["Logging:LogLevel:Orleans"] = "Warning",
            ["Logging:LogLevel:Grpc"] = "Warning",
            ["Logging:LogLevel:Grpc.AspNetCore"] = "Warning",
            ["Logging:LogLevel:System.Net.Http"] = "Warning",
            ["Logging:LogLevel:Microsoft.Extensions.Http"] = "Warning",
            // Polly is the resilience pipeline behind AddStandardResilienceHandler;
            // it logs every HttpClient execution attempt at Information.
            ["Logging:LogLevel:Polly"] = "Warning",
            ["Logging:LogLevel:StackExchange.Redis"] = "Warning",
            ["Logging:LogLevel:Aspire"] = "Warning",
            ["Logging:LogLevel:Microsoft.EntityFrameworkCore"] = "Warning",
        };

        builder.Configuration.Sources.Insert(
            0, new MemoryConfigurationSource { InitialData = defaults });
        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                // "DigitalBrain" is the single Meter for spec-declared neuron
                // counters/histograms and the base-class IAW metrics. MUST
                // match DigitalBrain.Core.Diagnostics.DigitalBrainTelemetry.SourceName
                // (ServiceDefaults has no project ref to Core). Without this
                // the @telemetry: instruments are created but never exported.
                metrics.AddMeter("DigitalBrain")
                    .AddMeter("Experimental.Microsoft.Extensions.AI")
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                // "DigitalBrain" is the single ActivitySource for neuron/gateway/
                // cortex/creator/LLM spans. It MUST match
                // DigitalBrain.Core.Diagnostics.DigitalBrainTelemetry.SourceName
                // (ServiceDefaults has no project ref to Core). Without this
                // line OpenTelemetry never listens to that source and every
                // DigitalBrain span is silently dropped.
                tracing.AddSource("DigitalBrain")
                    .AddSource("Experimental.Microsoft.Extensions.AI")
                    .AddSource(builder.Environment.ApplicationName)
                    .AddAspNetCoreInstrumentation(opts => opts.Filter = ctx =>
                        !ctx.Request.Path.StartsWithSegments(HealthEndpointPath)
                        && !ctx.Request.Path.StartsWithSegments(AlivenessEndpointPath))
                    .AddHttpClientInstrumentation();
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);
        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.MapHealthChecks(HealthEndpointPath);
            app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
            {
                Predicate = r => r.Tags.Contains("live"),
            });
        }
        return app;
    }


}

// One intentional, high-signal "{ResourceName} started" line per resource,
// emitted once the host is fully up. This REPLACES the framework lifetime
// chatter (floored to Warning in ApplyDigitalBrainLogDefaults) as the way both a
// human and an agent see "it's up" on a fresh `aspire start`.
//
// The logger category is the literal "DigitalBrain.Runtime" — NOT ILogger<T> —
// because this type lives in the Microsoft.Extensions.Hosting namespace and a
// type-derived category would be floored by the Microsoft=Warning rule,
// swallowing the very line we want. ResourceName is the project name with the
// "DigitalBrain." prefix stripped: DigitalBrain.Kernel -> "Kernel",
// DigitalBrain.SDK.Ai -> "Domains.Ai".
internal sealed class ResourceReadyLogger(
    IHostApplicationLifetime lifetime,
    IHostEnvironment environment,
    ILoggerFactory loggerFactory) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        lifetime.ApplicationStarted.Register(() =>
        {
            const string prefix = "DigitalBrain.";
            var name = environment.ApplicationName;
            var resourceName = name.StartsWith(prefix, StringComparison.Ordinal)
                ? name[prefix.Length..]
                : name;

            loggerFactory
                .CreateLogger("DigitalBrain.Runtime")
                .LogInformation("{ResourceName} started", resourceName);
        });
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
