using Aspire.Hosting;
using DigitalBrain;
using DigitalBrain.Core;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Behaviors;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Aspire.Hosting;
using DigitalBrain.Google;
using DigitalBrain.Time;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Aspire.Hosting.ApplicationModel;

var builder = DistributedApplication.CreateBuilder(args);
var testing = builder.Configuration.GetValue<bool>("DigitalBrain:Testing:Enabled");
var definitions = builder.Configuration[ApplicationConfigurationTransport.ConfigurationKey] is { } envelope
    ? ApplicationConfigurationTransport.Read(envelope, new DigitalBrainConfiguration().Modules)
    : DigitalBrainConfiguration.Bind(builder.Configuration).Modules;
var digitalBrain = builder.AddDigitalBrain(ProductSurfaceResources.Modules, persistentStorage: !testing)
    .AddModules(definitions);
var options = DigitalBrainConfiguration.Bind(builder.Configuration);

var clusterId = builder.Configuration["Orleans:ClusterId"]
    ?? (builder.Environment.IsDevelopment() ? $"digitalbrain-{Guid.NewGuid():N}" : null);

var runtime = builder.AddProject<Projects.IntoChat>(ProductSurfaceResources.IntoChat)
    .WithReference(digitalBrain)
    .WithEnvironment("OTEL_DOTNET_EXPERIMENTAL_ASPNETCORE_DISABLE_URL_QUERY_REDACTION", "false")
    .WithEnvironment("OTEL_DOTNET_EXPERIMENTAL_HTTPCLIENT_DISABLE_URL_QUERY_REDACTION", "false")
    .WithHttpEndpoint(
        port: testing ? null : ProductSurfaceResources.UiHttpPort,
        name: "http",
        isProxied: false)
    .WithHttpHealthCheck("/health", endpointName: "http")
    .AsPrimaryBrain()
    .WithUrlForEndpoint(
        "http",
        endpoint => new ResourceUrlAnnotation
        {
            Url = "/orleans",
            DisplayText = "Orleans Dashboard",
            Endpoint = endpoint,
        })
    .WithEnvironment(context =>
    {
        if (clusterId is not null)
        {
            context.EnvironmentVariables["Orleans__ClusterId"] = clusterId;
            context.EnvironmentVariables["Orleans__ServiceId"] = clusterId;
        }

        // Browser shell (aspire run and Playwright e2e) is a different origin than the kernel.
        // IsRunMode is false under DistributedApplicationTestingBuilder, so the origin must
        // always be advertised — not only when `aspire run` is driving the host.
        if (options.Flutter.Hosting.Kind == FlutterHostKind.Web)
        {
            context.EnvironmentVariables["DigitalBrain__Cors__AllowedOrigin"] =
                builder.CreateResourceBuilder<ExecutableResource>(options.Flutter.Hosting.ResourceName).GetEndpoint("http");
        }

    });

builder.Build().Run();
