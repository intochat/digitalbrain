using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Behaviors;

const string Silo = "silo";
const string BehaviorHost = "behavior-host";
const string KnownStateProtectionKey = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

var builder = DistributedApplication.CreateBuilder(args);

var brain = builder.AddDigitalBrain("brain");
brain.AddModule<BehaviorsModule>();

var silo = builder.AddProject<Projects.DigitalBrain_OS_Host>(Silo)
    .WithReference(brain)
    .WithEnvironment(
        DigitalBrainHostingExtensions.StateProtectionKeyConfigurationKey.Replace(":", "__", StringComparison.Ordinal),
        KnownStateProtectionKey)
    .WithEnvironment(
        BehaviorsModule.ExecutorConfigurationKey.Replace(":", "__", StringComparison.Ordinal),
        BehaviorsModule.HostExecutorName);

var behaviorHost = builder.AddProject<Projects.DigitalBrain_OS_BehaviorHost>(BehaviorHost)
    .WithReference(brain.AsClient())
    .WithEnvironment(
        DigitalBrainHostingExtensions.StateProtectionKeyConfigurationKey.Replace(":", "__", StringComparison.Ordinal),
        KnownStateProtectionKey)
    .WithHttpHealthCheck("/health")
    .WaitFor(silo);

silo.WithReference(behaviorHost)
    .WithEnvironment(
        BehaviorsModule.HostBaseAddressConfigurationKey.Replace(":", "__", StringComparison.Ordinal),
        behaviorHost.GetEndpoint("http"));

builder.Build().Run();
