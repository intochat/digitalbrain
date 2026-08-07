using DigitalBrain.AI;
using DigitalBrain.AI.Aspire.Hosting;
using DigitalBrain.AI.Ollama;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Assistant;
using DigitalBrain.Chat;
using DigitalBrain.Google;
using DigitalBrain.Google.Aspire.Hosting;
using DigitalBrain.Introspection;
using DigitalBrain.Memory;
using DigitalBrain.Memory.Aspire.Hosting;
using DigitalBrain.Salesforce;
using DigitalBrain.Salesforce.Aspire.Hosting;
using DigitalBrain.Shell;
using DigitalBrain.Shell.Aspire.Hosting;
using DigitalBrain.Tasks;
using DigitalBrain.Time;
using Microsoft.Extensions.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var brain = builder
    .AddDigitalBrain(ProductSurfaceResources.Brain)
    .WithLocalDevelopmentOAuthCallback(new Uri(ProductSurfaceResources.LocalDevelopmentOAuthCallbackUri));

brain.AddModule<AIModule>(ai =>
{
    ai.EnableSensitiveData = builder.Environment.IsDevelopment();
    ai.WithLlm<Gemma4>();
    ai.WithLlm<Llama32>();
});
brain.AddModule<ChatModule>();
brain.AddModule<MemoryModule>(memory => memory.WithQdrant());
brain.AddModule<AssistantModule>();
brain.AddModule<ShellModule>(shell => shell

    .WithWindowHost()
    );
brain.AddModule<GoogleModule>(google => google.WithGmail());
brain.AddModule<SalesforceModule>(salesforce => salesforce.WithSalesforce());
brain.AddModule<TasksModule>();
brain.AddModule<TimeModule>();
brain.AddModule<IntrospectionModule>();

var silo = builder.AddProject<Projects.DigitalBrain_Kernel>(ProductSurfaceResources.Silo)
    .WithReference(brain)
    .WithEnvironment(
        ShellHostingExtensions.OwnerEnvironmentVariable,
        ShellHostingExtensions.DefaultOwner)
    .WithHttpEndpoint(
        port: ProductSurfaceResources.UiHttpPort,
        name: ShellHostingExtensions.HttpEndpointName,
        isProxied: false)
    .WithHttpHealthCheck("/health");

var mcp = builder.AddProject<Projects.DigitalBrain_Mcp>(ProductSurfaceResources.Mcp)
    .WithReference(brain.AsClient())
    .WithEnvironment(
        ShellHostingExtensions.OwnerEnvironmentVariable,
        ShellHostingExtensions.DefaultOwner)
    .WithHttpEndpoint(
        port: ProductSurfaceResources.McpHttpPort,
        name: ProductSurfaceResources.McpHttpEndpointName)
    .WithHttpHealthCheck("/health", endpointName: ProductSurfaceResources.McpHttpEndpointName)
    .WaitFor(silo);

#pragma warning disable ASPIREMCP001
mcp.WithMcpServer(
    ProductSurfaceResources.McpPath,
    ProductSurfaceResources.McpHttpEndpointName);
#pragma warning restore ASPIREMCP001

builder.AddProject<Projects.DigitalBrain_Scripting>(ProductSurfaceResources.Scripting)
    .WithReference(brain.AsClient())
    .WithEnvironment(
        ShellHostingExtensions.OwnerEnvironmentVariable,
        ShellHostingExtensions.DefaultOwner)
    .WaitFor(silo);

builder.Build().Run();
