using DigitalBrain.Units;

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
        if (record is null || !record.Bindings.TryGetValue(slot.Name, out var name)) { throw new SlotUnboundException(state.Type, slot.Name); }
        if (os.Capabilities.Find(slot.Contract, name) is { } fake) { return fake; }

        // Not a fake: a connection of the host's ICapabilityConnections, as on the silo.
        if (await TestConnections.Problem(os.Options.Services, state, slot, name, ct).ConfigureAwait(false) is not null) { throw new SlotUnboundException(state.Type, slot.Name); }
        return await TestConnections.Resolve(os.Options.Services!, state, slot, name, ct).ConfigureAwait(false);
    }
}
