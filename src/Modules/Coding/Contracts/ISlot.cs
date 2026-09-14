using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Neurons;
using Orleans.Concurrency;

namespace DigitalBrain.Coding;

[Alias("slot")]
public interface ISlot : INeuron
{
    [Alias("build")]
    Task<Accepted<SlotReceipt>> Build(BuildSlot command);

    [Alias("promote")]
    Task<Accepted<SlotReceipt>> Promote(PromoteSlot command);

    [Alias("retire")]
    Task<Accepted<SlotReceipt>> Retire(RetireSlot command);

    [ReadOnly]
    [Alias("read")]
    Task<SlotSnapshot> Read();
}
