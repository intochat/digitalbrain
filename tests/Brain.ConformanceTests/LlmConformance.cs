using Brain.KernelTests;
using Brain.Modules.Sdk;

namespace Brain.ConformanceTests;

public sealed class LlmConformance(BrainClusterFixture<AiKindsConfigurator> fixture)
    : KindConformance<AiKindsConfigurator>(fixture)
{
    protected override string KindName => "llm";
    protected override string SampleContract => "llm.complete.v1";
    protected override string SampleInputJson => """{"prompt":"conform"}""";
    protected override string NeuronId => $"balanced-{Guid.NewGuid():N}";
}
