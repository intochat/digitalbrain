using System.Security.Cryptography;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Publishing;
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
using DigitalBrain.Memory;
using DigitalBrain.Memory.Aspire.Hosting;
using DigitalBrain.OS;
using DigitalBrain.Salesforce;
using DigitalBrain.Salesforce.Aspire.Hosting;
using DigitalBrain.Tasks;
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
brain.AddModule<MemoryModule>(memory => memory.WithQdrant());
brain.AddModule<OSBehaviorsModule>();
brain.AddModule<FlutterModule>(flutter => flutter
    .WithUiEdge()
     .WithHeadlessHost() // pure-Dart host; swap with window for headless-only dev
                         //.WithWindowHost()
    );
brain.AddModule<GoogleModule>(google => google.WithGmail());
brain.AddModule<SalesforceModule>(salesforce => salesforce.WithSalesforce());
brain.AddModule<BehaviorsModule>();
brain.AddModule<TasksModule>();

var behaviorBrokerCredential = builder.ExecutionContext.IsRunMode
    ? builder.AddParameter(
        "behavior-broker-credential",
        new BehaviorBrokerCredentialParameterDefault(),
        secret: true,
        persist: true)
    : builder.AddParameter("behavior-broker-credential", secret: true);
behaviorBrokerCredential.WithDescription(
    "Shared service credential for the BehaviorHost → silo reverse payload broker. Not an owner identity.");

var silo = builder.AddProject<Projects.DigitalBrain_OS_Host>(ProductSurfaceResources.Silo)
    .WithReference(brain)
    .WithEnvironment(
        BehaviorsModule.ExecutorConfigurationKey.Replace(":", "__", StringComparison.Ordinal),
        BehaviorsModule.HostExecutorName)
    .WithEnvironment(
        BehaviorBrokerContract.CredentialConfigurationKey.Replace(":", "__", StringComparison.Ordinal),
        behaviorBrokerCredential)
    .WithHttpHealthCheck("/health");

var behaviorHost = builder.AddProject<Projects.DigitalBrain_OS_BehaviorHost>(ProductSurfaceResources.BehaviorHost)
    .WithReference(brain.AsClient())
    .WithStateProtectionKey(brain)
    .WithEnvironment(
        BehaviorHostHosting.BrokerBaseAddressConfigurationKey.Replace(":", "__", StringComparison.Ordinal),
        silo.GetEndpoint("http"))
    .WithEnvironment(
        BehaviorBrokerContract.CredentialConfigurationKey.Replace(":", "__", StringComparison.Ordinal),
        behaviorBrokerCredential)
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

file sealed class BehaviorBrokerCredentialParameterDefault : ParameterDefault
{
    public override string GetDefaultValue()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public override void WriteToManifest(ManifestPublishingContext context)
        => throw new InvalidOperationException("Local behavior-broker credential defaults cannot be published.");
}
