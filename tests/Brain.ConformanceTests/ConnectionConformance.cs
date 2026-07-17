using Brain.KernelTests;
using Brain.Modules.Sdk;

namespace Brain.ConformanceTests;

public sealed class ConnectionConformance(BrainClusterFixture<ConnectionsKindsConfigurator> fixture)
    : KindConformance<ConnectionsKindsConfigurator>(fixture)
{
    protected override string KindName => "connection";
    protected override string SampleContract => "connection.probe.v1";
    protected override string SampleInputJson => "{}";
    protected override string NeuronId => $"google-{Guid.NewGuid():N}";
}
