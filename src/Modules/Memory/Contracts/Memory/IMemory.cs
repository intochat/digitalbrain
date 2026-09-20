using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.Memory;

[Alias("memory")]
public interface IMemory : INeuron
{
    Task<MemoryKey> Remember(Remember note);

    Task<MemoryKey> Forget(Forget note);

    [ReadOnly]
    Task<RecallResult> Recall(Recall query);
}
