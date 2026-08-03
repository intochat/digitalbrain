using System.Security.Cryptography;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Publishing;
using DigitalBrain.AI;
using DigitalBrain.AI.Aspire.Hosting;
using DigitalBrain.AI.Ollama;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Behaviors;
using DigitalBrain.Behaviors.Host;
using DigitalBrain.Behaviors.Runtime;
using DigitalBrain.Chat;
using DigitalBrain.Google;
using DigitalBrain.Google.Aspire.Hosting;
using DigitalBrain.Introspection;
using DigitalBrain.Memory;
using DigitalBrain.Memory.Aspire.Hosting;
using DigitalBrain.OS.Assistant;
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
    .WithUiEdge(ui => ui.HttpPort = ProductSurfaceResources.UiHttpPort)
    //.WithHeadlessHost() // pure-Dart host; swap with window for headless-only dev
    //.WithWebHost() // deploy UX: flutter run -d chrome under shell/; local default stays window
    .WithWindowHost()
    );
brain.AddModule<GoogleModule>(google => google.WithGmail());
brain.AddModule<SalesforceModule>(salesforce => salesforce.WithSalesforce());
brain.AddModule<BehaviorsModule>();
brain.AddModule<TasksModule>();
brain.AddModule<TimeModule>();
brain.AddModule<IntrospectionModule>();

var behaviorBrokerCredential = builder.ExecutionContext.IsRunMode
    ? builder.AddParameter(
        "behavior-broker-credential",
        new BehaviorBrokerCredentialParameterDefault(),
        secret: true,
        persist: true)
    : builder.AddParameter("behavior-broker-credential", secret: true);
behaviorBrokerCredential.WithDescription(
    "Shared service credential for the BehaviorHost → silo reverse payload broker. Not an owner identity.");

// Option A process boundary: distinct project resources — not co-hosted in one process.
// Silo residual executor is Host (HTTP to BehaviorHost); authored load is BehaviorHost-only.
var silo = builder.AddProject<Projects.DigitalBrain_OS_Host>(ProductSurfaceResources.Silo)
    .WithReference(brain)
    .WithEnvironment(
        BehaviorsModule.ExecutorConfigurationKey.Replace(":", "__", StringComparison.Ordinal),
        BehaviorsModule.HostExecutorName)
    .WithEnvironment(
        BehaviorBrokerContract.CredentialConfigurationKey.Replace(":", "__", StringComparison.Ordinal),
        behaviorBrokerCredential)
    .WithEnvironment(
        ShellHostingExtensions.OwnerEnvironmentVariable,
        ShellHostingExtensions.DefaultOwner)
    .WithHttpEndpoint(
        port: ProductSurfaceResources.McpHttpPort,
        name: ProductSurfaceResources.McpHttpEndpointName,
        isProxied: false)
    .WithHttpHealthCheck("/health");

#pragma warning disable ASPIREMCP001
silo.WithMcpServer(
    ProductSurfaceResources.McpPath,
    ProductSurfaceResources.McpHttpEndpointName);
#pragma warning restore ASPIREMCP001

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
        ShellHostingExtensions.OwnerEnvironmentVariable,
        ShellHostingExtensions.DefaultOwner);

silo.WithReference(behaviorHost)
    .WithEnvironment(
        BehaviorsModule.HostBaseAddressConfigurationKey.Replace(":", "__", StringComparison.Ordinal),
        behaviorHost.GetEndpoint("http"));

builder.Build().Run();

file sealed class BehaviorBrokerCredentialParameterDefault : ParameterDefault
{
    public override string GetDefaultValue()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public override void WriteToManifest(ManifestPublishingContext context)
        => throw new InvalidOperationException("Local behavior-broker credential defaults cannot be published.");
}
