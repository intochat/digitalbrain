using DigitalBrain.Abstractions.Signals;
using Orleans.Concurrency;

namespace DigitalBrain.Abstractions.Neurons;

// Host query only. All mutations still arrive as signals on IBehaviors.
// This avoids nested Brain.Send calls from an assistant turn.
[Alias("db.behaviors-kernel")]
public interface IBehaviorsKernel : IGrainWithStringKey
{
    [ReadOnly]
    [AlwaysInterleave]
    Task<IReadOnlyList<BehaviorDefinition>> ReadCurrent();
}
