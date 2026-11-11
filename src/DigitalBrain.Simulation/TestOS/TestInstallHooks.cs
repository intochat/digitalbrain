using System.Reflection;
using DigitalBrain.Kernel;
using DigitalBrain.Units;
using DigitalBrain.Time;

namespace DigitalBrain.Testing;

// The install hooks of a TestOS: a state neuron's reaction edges are its one brain-wide synapse in the registry (what
// Context.Synapses.EnsureBrainWide of its host holds), and its schedules are registrations with {brain}/time/default owned by its alias.
internal sealed class TestInstallHooks(TestOS os) : IInstallHooks
{
    private const string TimeType = "time";

    public Task EnsureReactionEdges(NeuronId state, IReadOnlyList<Type> signals, CancellationToken ct) => EnsureReactionEdges(state, signals, [], ct);

    // Alias reactions share the brain-wide entry, matched by each journaled signal's [Signal] alias.
    public Task EnsureReactionEdges(NeuronId state, IReadOnlyList<Type> signals, IReadOnlyList<string> signalAliases, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(signals);
        ArgumentNullException.ThrowIfNull(signalAliases);
        return signals.Count == 0 && signalAliases.Count == 0
            ? Task.CompletedTask
            : os.Delivery.EnsureBrainWide(os.Host(state), [.. signals], exact: true, aliases: signalAliases);
    }

    public Task RemoveReactionEdges(NeuronId state, CancellationToken ct)
        => os.Delivery.Remove(state, SynapseRegistry.BrainWideOf(state.Brain));

    // Replaces the alias's registrations only when they differ, so an unchanged install leaves its reminders running.
    public Task RegisterSchedules(NeuronId state, IReadOnlyList<Schedule> schedules, CancellationToken ct)
        => ScheduleDiff.Apply(Time(state.Brain), state.Type, schedules, ct);

    public Task UnregisterSchedules(NeuronId state, CancellationToken ct) => Time(state.Brain).Unregister(state.Type, ct);

    // A binding is the name of a capability fake bound for the slot's contract.
    public Task<string?> ValidateBinding(NeuronId state, SlotDescriptor slot, string binding, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(slot);
        return os.Capabilities.Find(slot.Contract, binding) is not null
            ? Task.FromResult<string?>(null)
            : TestConnections.Problem(os.Options.Services, state, slot, binding, ct);
    }

    private ITime Time(BrainId brain) => os.Get<ITime>(new NeuronId(brain, TimeType, "default"));
}
