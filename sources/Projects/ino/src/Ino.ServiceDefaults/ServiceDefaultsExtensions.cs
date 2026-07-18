using System.Diagnostics;
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

namespace Ino.ServiceDefaults;

/// <summary>
/// Aspire canonical ServiceDefaults pattern for ino. Every Host project
/// (Ino.System.Host, Ino.Identity.Host, Ino.Domains.Host — and any later
/// marketplace bundle that stands up its own silo) calls
/// <see cref="AddServiceDefaults{TBuilder}"/> once in <c>Program.cs</c>.
/// WebApplication hosts additionally call <see cref="MapDefaultEndpoints"/>
/// after <c>builder.Build()</c> to expose <c>/health</c> and <c>/alive</c>.
///
/// What this gives every silo:
///   * OpenTelemetry traces/metrics/logs with OTLP export (wired by Aspire
///     via the standard OTEL_EXPORTER_OTLP_ENDPOINT env var it already sets)
///   * Custom "Ino" / "Ino.*" ActivitySource + Meter registration so
///     neurons/gateway instrumentation shows up in the Aspire dashboard
///     without per-silo wiring
///   * Health checks (<c>/health</c>, <c>/alive</c>) for WebApplication hosts
///   * Service discovery wired into every HttpClient
///   * Standard HTTP resilience (retry + circuit breaker + timeout) on every
///     outbound HttpClient
///   * Conditional in-process ActivityListener when
///     <c>INO_TEST_MODE=true</c> — lit up by the trace-based E2E tests so
///     the fixture can assert the full cross-silo span chain without
///     talking to a real OTLP collector.
/// </summary>
public static class ServiceDefaultsExtensions
{
    /// <summary>
    /// Environment variable that enables the in-process trace capture. The
    /// trace-based E2E fixture sets this before booting the AppHost.
    /// </summary>
    public const string TestModeEnvVar = "INO_TEST_MODE";

    /// <summary>
    /// Telemetry source + meter prefix for all ino-authored instrumentation —
    /// lowercase <c>"ino"</c> to match <c>Ino.Core.Hosting.Telemetry.ActivitySourceName</c>.
    /// Kept in sync by hand for now; Phase 3 source generator will derive this
    /// from the interface contract.
    /// </summary>
    public const string InoTelemetryPrefix = "ino";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
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

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                serviceName: builder.Environment.ApplicationName,
                serviceNamespace: InoTelemetryPrefix))
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    // Any Meter named "Ino" or "Ino.*" (gateway, neurons, ml)
                    // is surfaced to Aspire without per-silo AddMeter wiring.
                    .AddMeter(InoTelemetryPrefix)
                    .AddMeter($"{InoTelemetryPrefix}.*");
            })
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    // ASP.NET Core instrumentation covers inbound gRPC (server
                    // spans for /ino.v1.Ino/Chat) and HttpClient covers
                    // outbound — gRPC.Net.Client runs over SocketsHttpHandler,
                    // so its spans flow through HttpClientInstrumentation
                    // without double-counting. A dedicated
                    // AddGrpcClientInstrumentation would add redundant spans;
                    // skip it.
                    .AddSource(builder.Environment.ApplicationName)
                    .AddSource(InoTelemetryPrefix)
                    .AddSource($"{InoTelemetryPrefix}.*");
            });

        builder.AddOpenTelemetryExporters();
        builder.AddInoTestActivityCapture();
        return builder;
    }

    static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }
        return builder;
    }

    static TBuilder AddInoTestActivityCapture<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        var testMode = string.Equals(
            builder.Configuration[TestModeEnvVar],
            "true",
            StringComparison.OrdinalIgnoreCase);
        if (!testMode) return builder;

        builder.Services.AddSingleton(InoTestTelemetryCapture.Instance);
        builder.Services.AddHostedService<InoTestActivityCaptureService>();
        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services
            .AddHealthChecks()
            // "live" tag = liveness probe (process is up). Detailed checks
            // that depend on downstream resources (Orleans cluster, OTLP
            // collector, etc.) should be tagged "ready" and exposed
            // separately — not in scope for slice 1.3.5.
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // Always expose — the POC is never production yet, and the trace-based
        // E2E test relies on /health to assert the silo finished startup
        // before the Flutter client begins sending traffic.
        app.MapHealthChecks("/health");
        app.MapHealthChecks("/alive", new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("live"),
        });
        return app;
    }
}

/// <summary>
/// Hosted service that registers an <see cref="ActivityListener"/> subscribed
/// to the "Ino" / "Ino.*" sources plus ASP.NET Core + HttpClient + Grpc.Net
/// default sources. Every stopped activity is appended to
/// <see cref="InoTestTelemetryCapture.Spans"/>. The listener disposes on
/// service stop so Clear()-then-Run cycles don't double-capture.
/// </summary>
internal sealed class InoTestActivityCaptureService : IHostedService, IDisposable
{
    readonly InoTestTelemetryCapture _capture;
    ActivityListener? _listener;

    public InoTestActivityCaptureService(InoTestTelemetryCapture capture) => _capture = capture;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source =>
                source.Name.StartsWith(ServiceDefaultsExtensions.InoTelemetryPrefix, StringComparison.OrdinalIgnoreCase)
                || source.Name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
                || source.Name.StartsWith("System.Net.Http", StringComparison.Ordinal)
                || source.Name.StartsWith("OpenTelemetry", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => _capture.Spans.Add(activity),
        };
        ActivitySource.AddActivityListener(_listener);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _listener?.Dispose();
        _listener = null;
    }
}
