using Aspire.Hosting;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Google;
using DigitalBrain.Google.Aspire.Hosting;
using DigitalBrain.Salesforce;
using DigitalBrain.Salesforce.Aspire.Hosting;
using Microsoft.Extensions.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var brain = builder.AddDigitalBrain(ProductSurfaceResources.Brain)
    .AddModule<GoogleModule>(google => google.WithGmail())
    .AddModule<SalesforceModule>(salesforce => salesforce.WithHostedMcp());
// Phase B: the module wiring these nine lines stand in for is in AppHost.cs at b225d085.
// Phase B: .AddModule<AIModule>(ai => ...)
// Phase B: .AddModule<MemoryModule>(memory => memory.WithQdrant())
// Phase B: .AddModule<TimeModule>()
// Phase B: .AddModule<ExcelModule>()
// Phase B: .AddModule<ExecutionModule>()
// Phase B: .AddModule<MicrosoftModule>(microsoft => ...)
// Phase B: .AddModule<UIModule>(ui => ui.WithWindowHost())

// Isolated Aspire runs reuse the persistent Azurite volume while assigning new random silo
// ports. A per-run development cluster avoids trying to contact a dead membership row from the
// previous run; the service id remains stable, so grain and reminder state are still preserved.
var developmentClusterId = builder.Environment.IsDevelopment()
    ? $"digitalbrain-{Guid.NewGuid():N}"
    : null;

builder.AddProject<Projects.DigitalBrain_Silo>(ProductSurfaceResources.Kernel)
    .WithReference(brain)
    .WithEnvironment("OTEL_DOTNET_EXPERIMENTAL_ASPNETCORE_DISABLE_URL_QUERY_REDACTION", "false")
    .WithEnvironment("OTEL_DOTNET_EXPERIMENTAL_HTTPCLIENT_DISABLE_URL_QUERY_REDACTION", "false")
    .WithHttpEndpoint(
        port: ProductSurfaceResources.UiHttpPort,
        name: "http",
        isProxied: false)
    // Without this, "kernel healthy" means only "process launched": Kestrel binds AFTER the
    // Orleans silo and brain activation finish, so waiters would proceed while 5080 still
    // refuses connections (observed on loaded CI runners).
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
    });

builder.Build().Run();
