using Brain.KernelTests;
using DigitalBrain.Tests;

namespace Brain.ConformanceTests;

public sealed class ChatConformance(BrainClusterFixture<ChatKindsConfigurator> fixture)
    : KindConformance<ChatKindsConfigurator>(fixture)
{
    protected override string KindName => "chat";
    protected override string SampleContract => "chat.post.v1";
    protected override string SampleInputJson => """{"text":"conform"}""";
}
