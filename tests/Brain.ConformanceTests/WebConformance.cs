using Brain.KernelTests;
using Brain.Modules.Sdk;

namespace Brain.ConformanceTests;

public sealed class WebConformance(BrainClusterFixture<WebKindsConfigurator> fixture)
    : KindConformance<WebKindsConfigurator>(fixture)
{
    protected override string KindName => "web";
    protected override string SampleContract => "web.fetch.v1";
    protected override string SampleInputJson => """{"url":"https://example.com/"}""";
}
