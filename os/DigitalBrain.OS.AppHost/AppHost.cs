using DigitalBrain.AI;
using DigitalBrain.AI.Aspire.Hosting;
using DigitalBrain.AI.Ollama;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Behaviors;
using DigitalBrain.Chat;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Aspire.Hosting;
using DigitalBrain.Google;
using DigitalBrain.Google.Aspire.Hosting;
using DigitalBrain.OS;
using DigitalBrain.Salesforce;
using DigitalBrain.Salesforce.Aspire.Hosting;
using Microsoft.Extensions.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var brain = builder.AddDigitalBrain(ProductSurfaceResources.Brain);

brain.AddModule<AIModule>(ai =>
{
    ai.EnableSensitiveData = builder.Environment.IsDevelopment();
    ai.WithLlm<Gemma4>();
    ai.WithLlm<Llama32>();
});
brain.AddModule<ChatModule>();
brain.AddModule<OSBehaviorsModule>();
brain.AddModule<FlutterModule>(flutter => flutter
    .WithUiEdge()
     .WithHeadlessHost() // pure-Dart host; swap with window for headless-only dev
                         //.WithWindowHost()
    );
brain.AddModule<GoogleModule>(google => google.WithGmail());
brain.AddModule<SalesforceModule>(salesforce => salesforce.WithSalesforce());
brain.AddModule<BehaviorsModule>();

var silo = builder.AddProject<Projects.DigitalBrain_OS_Host>(ProductSurfaceResources.Silo)
    .WithReference(brain)
    .WithEnvironment(
        BehaviorsModule.ExecutorConfigurationKey.Replace(":", "__", StringComparison.Ordinal),
        BehaviorsModule.HostExecutorName);

var behaviorHost = builder.AddProject<Projects.DigitalBrain_OS_BehaviorHost>(ProductSurfaceResources.BehaviorHost)
    .WithReference(brain.AsClient())
    .WithStateProtectionKey(brain)
    .WithHttpHealthCheck("/health")
    .WaitFor(silo)
    .WithEnvironment(
        FlutterHostingExtensions.OwnerEnvironmentVariable,
        FlutterHostingExtensions.DefaultOwner);

silo.WithReference(behaviorHost)
    .WithEnvironment(
        BehaviorsModule.HostBaseAddressConfigurationKey.Replace(":", "__", StringComparison.Ordinal),
        behaviorHost.GetEndpoint("http"));

#pragma warning disable ASPIREMCP001
builder.AddProject<Projects.DigitalBrain_OS_McpHost>(ProductSurfaceResources.Mcp)
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
