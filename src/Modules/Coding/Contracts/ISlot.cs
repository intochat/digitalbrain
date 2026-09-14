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

    // A build runs the whole solution build inside the slot's turn, and a read-only read still queues
    // behind it, so the default response timeout would expire on every poll of a slot that is merely busy.
    [ReadOnly]
    [Alias("read")]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    Task<SlotSnapshot> Read();
}
