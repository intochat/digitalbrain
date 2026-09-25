using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.CSharpExpert;

[Alias("csharp-expert.run")]
public interface ICodingRun : INeuron
{
    Task Request(FeatureRequest request);

    Task RecordContext(ProjectModel model);

    Task RecordPlan(CodingPlan plan);

    Task Fail(string reason);

    [ReadOnly]
    Task<CodingRunSnapshot> Read();
}
