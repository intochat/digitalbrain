using DigitalBrain.Kernel;
using DigitalBrain.Runtime.Orleans.Hosting;
using DigitalBrain.Units;
using DigitalBrain.Time;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Runtime.Orleans.Synapses;

/// <summary>
/// The install hooks of the silo: a state neuron's reaction edges are its one brain-wide synapse, set on its host grain through
/// <see cref="ISynapseHost.EnsureBrainWide"/> under the installs neuron's identity; its schedules are registrations with
/// <c>{brain}/time/default</c> owned by its alias.
/// </summary>
internal sealed class OrleansInstallHooks(IServiceProvider services) : IInstallHooks
{
    /// <summary>The neuron type of the installs neuron, <c>{brain}/synapse.installs/default</c>.</summary>
    internal const string InstallsType = "synapse.installs";

    private const string TimeType = "time";

    /// <inheritdoc />
    public Task EnsureReactionEdges(NeuronId state, IReadOnlyList<Type> signals, CancellationToken ct) => EnsureReactionEdges(state, signals, [], ct);

    /// <inheritdoc />
    /// <remarks>Typed reactions and alias reactions share the one brain-wide synapse; the edge holds journal aliases either way.</remarks>
    public async Task EnsureReactionEdges(NeuronId state, IReadOnlyList<Type> signals, IReadOnlyList<string> signalAliases, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(signals);
        ArgumentNullException.ThrowIfNull(signalAliases);
        if (signals.Count == 0 && signalAliases.Count == 0) { return; }
        var map = services.GetRequiredService<JournalPayloads>().Types;
        string[] aliases = [.. signals.Select(signal => map.TryAliasOf(signal, out var alias)
            ? alias
            : throw new ArgumentException($"{signal.Name} is not a registered [Signal] type.", nameof(signals))).Concat(signalAliases).Distinct(StringComparer.Ordinal)];
        await AsInstalls(state, host => host.EnsureBrainWide(aliases)).ConfigureAwait(true);
    }

    /// <inheritdoc />
    public Task RemoveReactionEdges(NeuronId state, CancellationToken ct)
    {
        return AsInstalls(state, host => host.RemoveBrainWide());
    }

    /// <inheritdoc />
    /// <remarks>The alias's registrations are replaced only when they differ, so an unchanged install leaves its reminders running.</remarks>
    public Task RegisterSchedules(NeuronId state, IReadOnlyList<Schedule> schedules, CancellationToken ct)
        => ScheduleDiff.Apply(Time(state.Brain), state.Type, schedules, ct);

    /// <inheritdoc />
    public Task UnregisterSchedules(NeuronId state, CancellationToken ct) => Time(state.Brain).Unregister(state.Type, ct);

    /// <inheritdoc />
    /// <remarks>A binding is a connection key the brain lists as connected and providing the slot's capability.</remarks>
    public async Task<string?> ValidateBinding(NeuronId state, SlotDescriptor slot, string binding, CancellationToken ct)
        => (await BrokerSlotResolver.Find(services, state, slot, binding, ct).ConfigureAwait(true)).Problem;

    private ITime Time(BrainId brain) => NeuronHosting.Get<ITime>(services, new NeuronId(brain, TimeType, "default"));

    // The state host admits a brain-wide edge only from the installs neuron of its brain.
    private async Task AsInstalls(NeuronId state, Func<ISynapseHost, Task> call)
    {
        var types = services.GetRequiredService<NeuronTypeRegistry>();
        var grainType = types.TryGetRegistration(state.Type, out var reg)
            ? NeuronTypeRegistry.GrainTypeOf(reg.Policy)
            : "neuron.host";
        var host = services.GetRequiredService<IGrainFactory>().GetGrain<ISynapseHost>(
            GrainId.Create(grainType, state.ToString()));
        var installs = new NeuronId(state.Brain, InstallsType, "default");
        var previous = RequestContext.Get(HostNeuronContext.CallerKey);
        RequestContext.Set(HostNeuronContext.CallerKey, installs.ToString());
        using var callerScope = Access.OrleansCallerAmbient.Instance.Enter(DigitalBrain.Access.Caller.OfKernel(state.Brain, via: installs));
        try { await call(host).ConfigureAwait(true); }
        finally
        {
            if (previous is null) { RequestContext.Remove(HostNeuronContext.CallerKey); }
            else { RequestContext.Set(HostNeuronContext.CallerKey, previous); }
        }
    }
}
