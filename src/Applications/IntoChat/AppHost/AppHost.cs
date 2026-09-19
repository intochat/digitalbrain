using Aspire.Hosting;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Aspire.Hosting;
using DigitalBrain.Time;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var digitalBrain = builder.AddDigitalBrain(ProductSurfaceResources.Modules)
    .AddModule<TimeModule>();

var elonSurface = string.Equals(
    builder.Configuration["Testing:Surface"],
    "elon",
    StringComparison.OrdinalIgnoreCase);
if (!builder.Configuration.GetValue("Testing:SkipFlutterHost", false)
    && (elonSurface || builder.Configuration["Testing:Surface"] is null or ""))
{
    digitalBrain.AddModule<FlutterModule>(module => module.WithWindowHost());
}

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

        if (builder.ExecutionContext.IsRunMode)
        {
            context.EnvironmentVariables["DigitalBrain__Cors__AllowedOrigin"] =
                $"http://{ShellNames.FlutterWebHostname}:{ShellNames.FlutterWebPort}";
        }
    });

builder.Build().Run();
