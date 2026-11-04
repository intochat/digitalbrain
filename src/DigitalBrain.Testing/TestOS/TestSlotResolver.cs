using DigitalBrain.Spi;

namespace DigitalBrain.Testing;

// The slot resolver of a TestOS: a slot's binding in the install record of its alias names a capability fake bound with
// TestOS.With<TCapability>(name, fake). No record, no binding or no such fake fails closed with SlotUnboundException.
internal sealed class TestSlotResolver(TestOS os) : ISlotResolver
{
    private const string InstallsType = "synapse.installs";

    public async Task<object> Resolve(NeuronId state, SlotDescriptor slot, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(slot);
        var record = await os.Get<IInstalls>(new NeuronId(state.Brain, InstallsType, "default")).Describe(state.Type, ct).ConfigureAwait(false);
        return record is not null && record.Bindings.TryGetValue(slot.Name, out var name) && os.Capabilities.Find(slot.Contract, name) is { } fake
            ? fake
            : throw new SlotUnboundException(state.Type, slot.Name);
    }
}
