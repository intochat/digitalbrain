using Brain.KernelTests;
using Brain.Modules.Sdk;

namespace Brain.ConformanceTests;

public sealed class GmailConformance(BrainClusterFixture<ConnectorsKindsConfigurator> fixture)
    : KindConformance<ConnectorsKindsConfigurator>(fixture)
{
    protected override string KindName => "gmail";
    protected override string SampleContract => "gmail.propose-send.v1";
    protected override string SampleInputJson => """{"to":"a@example.com","subject":"hi","body":"hello"}""";
}
