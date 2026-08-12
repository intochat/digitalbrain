using DigitalBrain.AI;
using DigitalBrain.AI.Aspire.Hosting;
using DigitalBrain.AI.Ollama;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Google;
using DigitalBrain.Google.Aspire.Hosting;
using DigitalBrain.Memory;
using DigitalBrain.Memory.Aspire.Hosting;
using DigitalBrain.Salesforce;
using DigitalBrain.Salesforce.Aspire.Hosting;
using DigitalBrain.UI;
using DigitalBrain.UI.Aspire.Hosting;
using Microsoft.Extensions.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var brain = builder
    .AddDigitalBrain(ProductSurfaceResources.Brain)
    .WithLocalDevelopmentOAuthCallback(new Uri(ProductSurfaceResources.LocalDevelopmentOAuthCallbackUri));

brain.AddModule<AIModule>(ai =>
{
    ai.EnableSensitiveData = builder.Environment.IsDevelopment();
    ai.WithLlm<Gemma4>();
    //ai.WithLlm<Llama32>();
});
brain.AddModule<MemoryModule>(memory => memory.WithQdrant());
brain.AddModule<UiModule>(ui => ui.WithWindowHost());
brain.AddModule<GoogleModule>(google => google.WithGmail());
brain.AddModule<SalesforceModule>(salesforce => salesforce.WithSalesforce());

var kernel = builder.AddProject<Projects.DigitalBrain_Kernel>(ProductSurfaceResources.Kernel)
    .WithReference(brain)
    .WithEnvironment(
        ShellHostingExtensions.OwnerEnvironmentVariable,
        ShellHostingExtensions.DefaultOwner)
    .WithHttpEndpoint(
        port: ProductSurfaceResources.UiHttpPort,
        name: ShellHostingExtensions.HttpEndpointName,
        isProxied: false)
    .WithHttpHealthCheck("/health")
    .WithUrlForEndpoint(
        ShellHostingExtensions.HttpEndpointName,
        endpoint => new ResourceUrlAnnotation
        {
            Url = "/orleans",
            DisplayText = "Orleans Dashboard",
            Endpoint = endpoint,
        });

var mcp = builder.AddProject<Projects.DigitalBrain_Mcp>(ProductSurfaceResources.Mcp)
    .WithReference(brain.AsClient())
    .WithEnvironment(
        ShellHostingExtensions.OwnerEnvironmentVariable,
        ShellHostingExtensions.DefaultOwner)
    .WithHttpEndpoint(
        port: ProductSurfaceResources.McpHttpPort,
        name: ProductSurfaceResources.McpHttpEndpointName)
    .WithHttpHealthCheck("/health", endpointName: ProductSurfaceResources.McpHttpEndpointName)
    .WaitFor(kernel);

#pragma warning disable ASPIREMCP001
mcp.WithMcpServer(
    ProductSurfaceResources.McpPath,
    ProductSurfaceResources.McpHttpEndpointName);
#pragma warning restore ASPIREMCP001

// Do not WaitFor(kernel): membership prune must run against Azurite before a new silo
// can join if a prior force-kill left an Active row. The probe waits on /health itself.
builder.AddProject<Projects.DigitalBrain_Scripting>(ProductSurfaceResources.Scripting)
    .WithReference(brain.AsClient())
    .WithEnvironment(
        ShellHostingExtensions.OwnerEnvironmentVariable,
        ShellHostingExtensions.DefaultOwner);

builder.Build().Run();
