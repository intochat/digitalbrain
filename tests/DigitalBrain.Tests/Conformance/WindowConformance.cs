using Brain.KernelTests;
using DigitalBrain.Tests;

namespace Brain.ConformanceTests;

public sealed class WindowConformance(BrainClusterFixture<WorkspaceKindsConfigurator> fixture)
    : KindConformance<WorkspaceKindsConfigurator>(fixture)
{
    protected override string KindName => "window";
    protected override string SampleContract => "window.render.v1";
    protected override string SampleInputJson => """{"version":1,"blocks":[]}""";
}
