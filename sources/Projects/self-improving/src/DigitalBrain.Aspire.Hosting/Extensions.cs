using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Orleans.Configuration;
using Orleans.Dashboard;

namespace Microsoft.Extensions.Hosting
{
    // Adds common Aspire services: service discovery, resilience, health checks, and OpenTelemetry.
    // This project should be referenced by each service project in your solution.
    // To learn more about using this project, see https://aka.ms/aspire/service-defaults
    public static class Extensions
    {
        private const string HealthEndpointPath = "/health";
        private const string AlivenessEndpointPath = "/alive";

        public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
        {
            builder.ConfigureOpenTelemetry();

            builder.AddDefaultHealthChecks();

            builder.Services.AddServiceDiscovery();

            builder.Services.ConfigureHttpClientDefaults(http =>
            {
                // Turn on resilience by default
                http.AddStandardResilienceHandler();

                // Turn on service discovery by default
                http.AddServiceDiscovery();
            });

            return builder;
        }

        public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
        {
            builder.Logging.AddOpenTelemetry(logging =>
            {
                logging.IncludeFormattedMessage = true;
                logging.IncludeScopes = true;
            });

            builder.Services.AddOpenTelemetry()
                .WithMetrics(metrics =>
                {
                    metrics.AddAspNetCoreInstrumentation()
                        .AddHttpClientInstrumentation()
                        .AddRuntimeInstrumentation()
                        .AddMeter("Microsoft.Orleans")
                        .AddMeter("DigitalBrain.Neuron");
                })
                .WithTracing(tracing =>
                {
                    tracing.AddSource(builder.Environment.ApplicationName)
                        .AddSource("Microsoft.Orleans.Runtime")
                        .AddSource("Microsoft.Orleans.Application")
                        .AddSource("DigitalBrain.Neuron")
                        .AddAspNetCoreInstrumentation(tracing =>
                            // Exclude health check requests from tracing
                            tracing.Filter = context =>
                                !context.Request.Path.StartsWithSegments(HealthEndpointPath)
                                && !context.Request.Path.StartsWithSegments(AlivenessEndpointPath)
                        )
                        .AddHttpClientInstrumentation();
                });

            builder.AddOpenTelemetryExporters();

            return builder;
        }

        private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
        {
            var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

            if (useOtlpExporter)
            {
                builder.Services.AddOpenTelemetry().UseOtlpExporter();
            }

            return builder;
        }

        public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
        {
            builder.Services.AddHealthChecks()
                // Add a default liveness check to ensure app is responsive
                .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

            return builder;
        }

        public static WebApplication MapDefaultEndpoints(this WebApplication app)
        {
            // Adding health checks endpoints to applications in non-development environments has security implications.
            // See https://aka.ms/aspire/healthchecks for details before enabling these endpoints in non-development environments.
            if (app.Environment.IsDevelopment())
            {
                // All health checks must pass for app to be considered ready to accept traffic after starting
                app.MapHealthChecks(HealthEndpointPath);

                // Only health checks tagged with the "live" tag must pass for app to be considered alive
                app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
                {
                    Predicate = r => r.Tags.Contains("live")
                });
            }

            return app;
        }

        // Extension kept inside Microsoft.Extensions.Hosting ns for the same minimal using discoverability as AddServiceDefaults.
        // Wires timeline streams, named stores, activity, and per-grain JournalStore (for Neuron Incoming/Outgoing journals + lifecycle) across start.cs / Kernel / tests / Aspire.
        public static ISiloBuilder ConfigureDigitalBrainDefaults(this ISiloBuilder silo)
        {
            new DigitalBrain.Aspire.Hosting.DigitalBrainSiloConfiguration().Configure(silo);
            return silo;
        }

        // Client equivalent: registers the timeline stream provider so TaskManagerClient / surfaces can sub "DigitalBrainTimeline".
        public static IClientBuilder ConfigureDigitalBrainClientDefaults(this IClientBuilder client)
        {
            client.AddStreaming();
            client.AddMemoryStreams("DigitalBrainTimeline");
            return client;
        }

        // Phase 1 unified cluster wiring (the reusable "kernel class lib setup").
        // Thin hosts, the Kernel project, start.cs REPL hosts, and Simulation call this (or the client form).
        // Centralizes Cluster/Endpoint/advertised/port/env/Sanitize/reminders + the DigitalBrain defaults.
        // Experiences (Awesome or future) are just additional assembly references on the host that invokes this; their sourcegen manifests participate in dispatch automatically.
        // Callers receive a DigitalBrainCluster (Sdk) as the "reference to pass".
        public static ISiloBuilder UseDigitalBrain(this ISiloBuilder silo, DigitalBrain.Aspire.Hosting.DigitalBrainSiloOptions? options = null)
        {
            options ??= new DigitalBrain.Aspire.Hosting.DigitalBrainSiloOptions();

            var world = options.WorldId ?? Environment.GetEnvironmentVariable("DIGITALBRAIN_WORLD_ID") ?? "primary";
            var clusterId = options.ClusterId ?? Environment.GetEnvironmentVariable("DIGITALBRAIN_CLUSTER_ID") ?? $"digitalbrain-{SanitizeForWiring(world)}";
            var serviceId = options.ServiceId;
            var siloPort = options.SiloPort ?? (int.TryParse(Environment.GetEnvironmentVariable("DIGITALBRAIN_SILO_PORT"), out var sp) ? sp : 11111);
            var gatewayPort = options.GatewayPort ?? (int.TryParse(Environment.GetEnvironmentVariable("DIGITALBRAIN_GATEWAY_PORT"), out var gp) ? gp : 30000);
            var advertised = options.AdvertisedIPAddress ?? (System.Net.IPAddress.TryParse(Environment.GetEnvironmentVariable("DIGITALBRAIN_ADVERTISED_IP"), out var ip) ? ip : System.Net.IPAddress.Loopback);

            silo.Configure<ClusterOptions>(o =>
            {
                o.ClusterId = clusterId;
                o.ServiceId = serviceId;
            });

            silo.UseLocalhostClustering();

            // mTLS: package in defaults (UseTls ready per official doc; dev LAN/Global peers).

            silo.Configure<EndpointOptions>(o =>
            {
                o.AdvertisedIPAddress = advertised;
                o.SiloPort = siloPort;
                o.GatewayPort = gatewayPort;
            });

            if (options.UseInMemoryReminders)
            {
                silo.UseInMemoryReminderService();
            }

            if (options.EnableOrleansDashboard)
            {
                silo.AddDashboard(dashboard =>
                {
                    dashboard.CounterUpdateIntervalMs = 1_000;
                    dashboard.HistoryLength = 120;
                });
            }

            silo.ConfigureDigitalBrainDefaults();
            return silo;
        }

        public static IClientBuilder UseDigitalBrainClient(this IClientBuilder client, string? clusterId = null, System.Net.IPEndPoint? gateway = null)
        {
            var effectiveCluster = clusterId ?? Environment.GetEnvironmentVariable("DIGITALBRAIN_CLUSTER_ID") ?? "digitalbrain-primary";
            client.Configure<ClusterOptions>(o =>
            {
                o.ClusterId = effectiveCluster;
                o.ServiceId = Environment.GetEnvironmentVariable("DIGITALBRAIN_SERVICE_ID") ?? "digitalbrain";
            });

            if (gateway is not null)
            {
                client.UseStaticClustering(gateway);
            }

            // mTLS for client side (MarketplacePeer / global peer connects use the TLS gateway; package enables UseTls on IClientBuilder).
            client.ConfigureDigitalBrainClientDefaults();
            return client;
        }

        private static string SanitizeForWiring(string value) =>
            new(value.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.').ToArray());

        public static IHostApplicationBuilder AddDigitalBrainLlms(this IHostApplicationBuilder builder)
        {
            var gemmaEndpoint = builder.Configuration.GetConnectionString("gemma") ?? Environment.GetEnvironmentVariable("GEMMA_ENDPOINT") ?? "http://localhost:11434";
            var nemotronEndpoint = builder.Configuration.GetConnectionString("nemotron") ?? Environment.GetEnvironmentVariable("NEMOTRON_ENDPOINT") ?? "http://localhost:11434";

            // Default / expressive model for plain IChatClient injection.
            // [LLM<Gemma4_26b>] is more expressive but this is what you get for "IChatClient" by default.
            // First run with 27b-class may take ~5 minutes to download the model.
            var defaultModel = Environment.GetEnvironmentVariable("DIGITALBRAIN_LLM_DEFAULT")
                ?? Environment.GetEnvironmentVariable("GEMMA_MODEL")
                ?? "gemma4:26b";   // ~26b class Gemma (marker Gemma4_26b); use gemma4:26b with pinned recent Ollama image

            var fastModel = Environment.GetEnvironmentVariable("DIGITALBRAIN_LLM_FAST") ?? "nemotron-3-nano";
            var balancedModel = Environment.GetEnvironmentVariable("DIGITALBRAIN_LLM_BALANCED") ?? "nemotron-3-nano";
            var reasoningModel = Environment.GetEnvironmentVariable("DIGITALBRAIN_LLM_REASONING") ?? "nemotron-3-nano";

            builder.Services.AddKeyedSingleton<IChatClient>("fast", (_, _) =>
                new OllamaChatClient(new Uri(gemmaEndpoint), fastModel));
            builder.Services.AddKeyedSingleton<IChatClient>("balanced", (_, _) =>
                new OllamaChatClient(new Uri(nemotronEndpoint), balancedModel));
            builder.Services.AddKeyedSingleton<IChatClient>("reasoning", (_, _) =>
                new OllamaChatClient(new Uri(nemotronEndpoint), reasoningModel));

            // Expressive large local model (used by default IChatClient and [LLM<Gemma4_26b>])
            builder.Services.AddKeyedSingleton<IChatClient>("gemma4-26b", (_, _) =>
                new OllamaChatClient(new Uri(gemmaEndpoint), defaultModel));

            builder.Services.AddKeyedSingleton<IChatClient>(fastModel, (sp, _) => sp.GetRequiredKeyedService<IChatClient>("fast"));
            builder.Services.AddKeyedSingleton<IChatClient>(balancedModel, (sp, _) => sp.GetRequiredKeyedService<IChatClient>("balanced"));
            builder.Services.AddKeyedSingleton<IChatClient>("Gemma4_26b", (sp, _) => sp.GetRequiredKeyedService<IChatClient>("gemma4-26b"));

            // Default IChatClient is the expressive Gemma4_26b (local Ollama). More specific [LLM<T>] can be used for documentation / future selector.
            builder.Services.AddSingleton<IChatClient>(sp => sp.GetRequiredKeyedService<IChatClient>("gemma4-26b"));

            return builder;
        }
    }
}

namespace DigitalBrain.Aspire.Hosting
{
    // Expressive model markers + attribute for LLM selection in neurons/agents.
    // Usage: public Foo([LLM<Gemma4_26b>] IChatClient llm)
    // The plain IChatClient singleton is wired to the expressive default (Gemma4_26b).
    public sealed record Gemma3_1B;
    public sealed record Gemma4_26b;

    [AttributeUsage(AttributeTargets.Parameter)]
    public sealed class LLMAttribute<TModel> : Attribute where TModel : notnull { }

    // The concrete holder for DigitalBrain silo defaults (timeline, named stores, activity for tracing).
    // The public mechanism is the ConfigureDigitalBrainDefaults extension on ISiloBuilder (called from any UseOrleans lambda).
    // No user-facing "implement this" interface — Orleans provides ISiloConfigurator only for its TestCluster; we use the extension for the common DigitalBrain contract across envs (test, solo, LAN, Aspire).
    // Providers legitimately differ by environment; the DigitalBrain bits (stream name, stores neurons expect, activity) are uniform so installed neurons behave the same.
    public sealed class DigitalBrainSiloConfiguration
    {
        public void Configure(ISiloBuilder silo)
        {
            silo.AddMemoryGrainStorage("Default")
                .AddMemoryGrainStorage("PubSubStore")
                .AddMemoryStreams("DigitalBrainTimeline")
                .AddActivityPropagation();

            // Custom Orleans.Journaling wiring (user override: "bring back journaling").
            // JournalStore provides per-NeuronId (per-grain) isolated IDurableList<Synapse> for Incoming/Outgoing on the Neuron base.
            // Lookup by Self on activate. In-mem for the custom path; Orleans 10.2 no longer loads the old preview IStateMachineManager shim.
            silo.Services.AddSingleton<DigitalBrain.Os.Infrastructure.Orleans.JournalStore>();
        }
    }

    // (DigitalBrainSiloOptions lives in its own .cs in this folder/namespace so it is the single definition; the wiring extensions above consume it.)
}
