using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Behaviors;
using DigitalBrain.Tasks;

const string Silo = "silo";
const string BehaviorHost = "behavior-host";
const string KnownStateProtectionKey = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
// TestingAppHost-only shared broker credential. Not a production secret; product uses a secret parameter.
const string KnownBrokerCredential = "testing-behavior-broker-credential-v1";

var builder = DistributedApplication.CreateBuilder(args);

var brain = builder.AddDigitalBrain("brain");
brain.AddModule<BehaviorsModule>();
brain.AddModule<TasksModule>();

var silo = builder.AddProject<Projects.DigitalBrain_OS_Host>(Silo)
    .WithReference(brain)
    .WithEnvironment(
        DigitalBrainHostingExtensions.StateProtectionKeyConfigurationKey.Replace(":", "__", StringComparison.Ordinal),
        KnownStateProtectionKey)
    .WithEnvironment(
        BehaviorsModule.ExecutorConfigurationKey.Replace(":", "__", StringComparison.Ordinal),
        BehaviorsModule.HostExecutorName)
    .WithEnvironment(
        BehaviorBrokerContract.CredentialConfigurationKey.Replace(":", "__", StringComparison.Ordinal),
        KnownBrokerCredential)
    .WithHttpHealthCheck("/health");

var behaviorHost = builder.AddProject<Projects.DigitalBrain_OS_BehaviorHost>(BehaviorHost)
    .WithReference(brain.AsClient())
    .WithEnvironment(
        DigitalBrainHostingExtensions.StateProtectionKeyConfigurationKey.Replace(":", "__", StringComparison.Ordinal),
        KnownStateProtectionKey)
    .WithEnvironment(
        BehaviorHostHosting.BrokerBaseAddressConfigurationKey.Replace(":", "__", StringComparison.Ordinal),
        silo.GetEndpoint("http"))
    .WithEnvironment(
        BehaviorBrokerContract.CredentialConfigurationKey.Replace(":", "__", StringComparison.Ordinal),
        KnownBrokerCredential)
    .WithHttpHealthCheck("/health")
    .WaitFor(silo);

silo.WithReference(behaviorHost)
    .WithEnvironment(
        BehaviorsModule.HostBaseAddressConfigurationKey.Replace(":", "__", StringComparison.Ordinal),
        behaviorHost.GetEndpoint("http"));

builder.Build().Run();
