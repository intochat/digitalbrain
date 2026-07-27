using DigitalBrain.AI;
using DigitalBrain.AI.Aspire.Hosting;
using DigitalBrain.AI.Ollama;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Chat;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Aspire.Hosting;
using DigitalBrain.Google;
using DigitalBrain.Google.Aspire.Hosting;
using DigitalBrain.Os;
using DigitalBrain.Salesforce;
using DigitalBrain.Salesforce.Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var brain = builder.AddDigitalBrain(ProductSurfaceResources.Brain);

brain.AddModule<AIModule>(ai => ai.WithLlm<Llama32>());
brain.AddModule<ChatModule>();
brain.AddModule<OsBehaviorsModule>();
brain.AddModule<FlutterModule>(flutter => flutter
    .WithUiEdge()
    .WithFlutterHost());
brain.AddModule<GoogleModule>(google => google.WithGmail());
brain.AddModule<SalesforceModule>(salesforce => salesforce.WithSalesforce());

var silo = builder.AddProject<Projects.DigitalBrain_Host>(ProductSurfaceResources.Silo)
    .WithReference(brain);

builder.AddProject<Projects.DigitalBrain_Mcp>(ProductSurfaceResources.Mcp)
    .WithReference(brain.AsClient())
    .WaitFor(silo)
    .WithEnvironment(
        FlutterHostingExtensions.OwnerEnvironmentVariable,
        FlutterHostingExtensions.DefaultOwner)
    .WithHttpEndpoint(
        port: ProductSurfaceResources.McpHttpPort,
        name: ProductSurfaceResources.McpHttpEndpointName,
        isProxied: false);

builder.Build().Run();
