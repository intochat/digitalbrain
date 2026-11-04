using DigitalBrain.Spi;
using DigitalBrain.Time;

namespace DigitalBrain.Testing;

// The install hooks of a TestOS: a state neuron's reaction edges are its one brain-wide synapse in the registry (what
// Context.Synapses.EnsureBrainWide of its host holds), and its schedules are registrations with {brain}/time/default owned by its alias.
internal sealed class TestInstallHooks(TestOS os) : IInstallHooks
{
    private const string TimeType = "time";

    public Task EnsureReactionEdges(NeuronId state, IReadOnlyList<Type> signals, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(signals);
        return signals.Count == 0 ? Task.CompletedTask : os.Delivery.EnsureBrainWide(os.Host(state), [.. signals], exact: true);
    }

    public Task RemoveReactionEdges(NeuronId state, CancellationToken ct)
    {
        os.Delivery.Remove(state, SynapseRegistry.BrainWideOf(state.Brain));
        return Task.CompletedTask;
    }

    // Replaces the alias's registrations only when they differ, so an unchanged install leaves its reminders running.
    public async Task RegisterSchedules(NeuronId state, IReadOnlyList<Schedule> schedules, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(schedules);
        var time = Time(state.Brain);
        var held = (await time.Registered(ct).ConfigureAwait(false)).Where(r => r.Owner == state.Type).Select(r => r.Schedule).ToHashSet(StringComparer.Ordinal);
        var wanted = schedules.Select(s => s.Text).ToHashSet(StringComparer.Ordinal);
        if (held.SetEquals(wanted)) { return; }
        if (held.Count > 0) { await time.Unregister(state.Type, ct).ConfigureAwait(false); }
        foreach (var text in wanted) { await time.Register(state.Type, text, ct).ConfigureAwait(false); }
    }

    public Task UnregisterSchedules(NeuronId state, CancellationToken ct) => Time(state.Brain).Unregister(state.Type, ct);

    // A binding is the name of a capability fake bound for the slot's contract.
    public Task<string?> ValidateBinding(NeuronId state, SlotDescriptor slot, string binding, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(slot);
        return Task.FromResult(os.Capabilities.Find(slot.Contract, binding) is null
            ? $"slot '{slot.Name}': no {slot.Capability} fake named '{binding}'"
            : null);
    }

    private ITime Time(BrainId brain) => os.Get<ITime>(new NeuronId(brain, TimeType, "default"));
}
