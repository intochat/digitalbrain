using DigitalBrain.AI;
using DigitalBrain.AI.Aspire.Hosting;
using DigitalBrain.AI.Ollama;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Chat;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Aspire.Hosting;
using DigitalBrain.Google;
using DigitalBrain.Google.Aspire.Hosting;
using DigitalBrain.OS;
using DigitalBrain.Salesforce;
using DigitalBrain.Salesforce.Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var brain = builder.AddDigitalBrain(ProductSurfaceResources.Brain);

brain.AddModule<AIModule>(ai => ai
    .WithLlm<Gemma4>());
brain.AddModule<ChatModule>();
brain.AddModule<OSBehaviorsModule>();
brain.AddModule<FlutterModule>(flutter => flutter
    .WithUIEdge()
    .WithFlutterHost());
brain.AddModule<GoogleModule>(google => google.WithGmail());
brain.AddModule<SalesforceModule>(salesforce => salesforce.WithSalesforce());

var silo = builder.AddProject<Projects.DigitalBrain_OS_Host>(ProductSurfaceResources.Silo)
    .WithReference(brain);

#pragma warning disable ASPIREMCP001
builder.AddProject<Projects.DigitalBrain_OS_Mcp>(ProductSurfaceResources.Mcp)
    .WithReference(brain.AsClient())
    .WaitFor(silo)
    .WithEnvironment(
        FlutterHostingExtensions.OwnerEnvironmentVariable,
        FlutterHostingExtensions.DefaultOwner)
    .WithHttpEndpoint(
        port: ProductSurfaceResources.McpHttpPort,
        name: ProductSurfaceResources.McpHttpEndpointName,
        isProxied: false)
    .WithMcpServer(
        ProductSurfaceResources.McpPath,
        ProductSurfaceResources.McpHttpEndpointName);
#pragma warning restore ASPIREMCP001

builder.Build().Run();
