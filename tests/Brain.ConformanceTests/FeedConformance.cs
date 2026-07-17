using Brain.KernelTests;
using Brain.Modules.Sdk;

namespace Brain.ConformanceTests;

public sealed class FeedConformance(BrainClusterFixture<WorkspaceKindsConfigurator> fixture)
    : KindConformance<WorkspaceKindsConfigurator>(fixture)
{
    protected override string KindName => "feed";
    protected override string SampleContract => "feed.append.v1";
    protected override string SampleInputJson => """{"sourceKey":"k","revision":1,"kind":"chat"}""";
}
