using Aspire.Hosting;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Behaviors;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Aspire.Hosting;
using DigitalBrain.Time;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var digitalBrain = builder.AddDigitalBrain(ProductSurfaceResources.Modules)
    .AddModule<TimeModule>()
    .AddModule<TestTwitterModule>();

digitalBrain.AddModule<FlutterModule>(module =>
{
    var kind = builder.Configuration.GetValue("DigitalBrain:Flutter:Hosting:Kind", FlutterHostKind.Window);
    if (kind == FlutterHostKind.None)
    {
        return;
    }

    if (kind == FlutterHostKind.Web)
    {
        module.WithWebHost();
    }
    else if (kind == FlutterHostKind.Headless)
    {
        module.WithHeadlessHost();
    }
    else
    {
        module.WithWindowHost();
    }
});

var developmentClusterId = builder.Environment.IsDevelopment()
    ? $"digitalbrain-{Guid.NewGuid():N}"
    : null;

builder.AddProject<Projects.IntoChat>(ProductSurfaceResources.IntoChat)
    .WithReference(digitalBrain)
    .WithEnvironment("OTEL_DOTNET_EXPERIMENTAL_ASPNETCORE_DISABLE_URL_QUERY_REDACTION", "false")
    .WithEnvironment("OTEL_DOTNET_EXPERIMENTAL_HTTPCLIENT_DISABLE_URL_QUERY_REDACTION", "false")
    .WithHttpEndpoint(
        port: ProductSurfaceResources.UiHttpPort,
        name: "http",
        isProxied: false)
    .WithHttpHealthCheck("/health", endpointName: "http")
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
        if (developmentClusterId is not null)
        {
            context.EnvironmentVariables["Orleans__ClusterId"] = developmentClusterId;
        }

        // Browser shell (aspire run and Playwright e2e) is a different origin than the kernel.
        // IsRunMode is false under DistributedApplicationTestingBuilder, so the origin must
        // always be advertised — not only when `aspire run` is driving the host.
        context.EnvironmentVariables["DigitalBrain__Cors__AllowedOrigin"] =
            $"http://{ShellNames.FlutterWebHostname}:{ShellNames.FlutterWebPort}";

    });

builder.Build().Run();
