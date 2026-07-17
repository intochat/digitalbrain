using System.Text.Json;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Brain.Kernel.Host;

public static class ServiceDefaults
{
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";
    private const string OAuthEndpointPath = "/oauth";

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
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter("Microsoft.Orleans")
                    .AddMeter("DigitalBrain.Neuron");
            })
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(builder.Environment.ApplicationName)
                    .AddSource("Microsoft.Orleans.Application")
                    .AddSource("DigitalBrain.Neuron")
                    .AddSource("DigitalBrain.Ino.Worker")
                    .AddSource("DigitalBrain.Ino.Workflow")
                    .AddSource("DigitalBrain.Ino.Outbox")
                    .AddAspNetCoreInstrumentation(options =>
                        options.Filter = context =>
                            !context.Request.Path.StartsWithSegments(HealthEndpointPath) &&
                            !context.Request.Path.StartsWithSegments(AlivenessEndpointPath) &&
                            !context.Request.Path.StartsWithSegments(OAuthEndpointPath))
                    .AddHttpClientInstrumentation();
            });
        builder.AddOpenTelemetryExporters();
        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services
            .AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);
        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks(
            HealthEndpointPath,
            new HealthCheckOptions { ResponseWriter = WriteSafeHealthResponseAsync });
        app.MapHealthChecks(
            AlivenessEndpointPath,
            new HealthCheckOptions
            {
                Predicate = registration => registration.Tags.Contains("live")
            });
        return app;
    }

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        if (!string.IsNullOrWhiteSpace(
                builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        if (!string.IsNullOrEmpty(
                builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
        {
            builder.Services.AddOpenTelemetry().UseAzureMonitor();
        }

        return builder;
    }

    private static Task WriteSafeHealthResponseAsync(
        HttpContext context,
        HealthReport report)
    {
        context.Response.ContentType = "application/json";
        var payload = new Dictionary<string, object?>
        {
            ["status"] = report.Status.ToString()
        };
        if (report.Entries.TryGetValue(
                "digitalbrain-runtime-state",
                out var runtimeState))
        {
            var safe = new Dictionary<string, object?>
            {
                ["status"] = runtimeState.Status.ToString()
            };
            foreach (var key in new[]
                     {
                         "backendKind",
                         "namespace",
                         "schemaVersion",
                         "keyVersion"
                     })
            {
                if (runtimeState.Data.TryGetValue(key, out var value))
                    safe[key] = value;
            }

            payload["runtimeState"] = safe;
        }

        return JsonSerializer.SerializeAsync(
            context.Response.Body,
            payload,
            cancellationToken: context.RequestAborted);
    }
}
