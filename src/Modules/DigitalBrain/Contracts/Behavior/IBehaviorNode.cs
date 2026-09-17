using DigitalBrain.Abstractions.Neurons;
using Orleans.Concurrency;

namespace DigitalBrain.Abstractions.Behavior;

[Alias("behavior-node")]
public interface IBehaviorNode : INeuron
{
    [Alias("configure")]
    Task Configure(string behaviorId, long version, BehaviorNode node, string trigger, IReadOnlyList<string> predecessors);

    [ReadOnly, Alias("read")]
    Task<BehaviorNodeRuntime?> Read();
}
