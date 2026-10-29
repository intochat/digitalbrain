using DigitalBrain.Contracts;
using DigitalBrain.Runtime.Orleans.Hosting;
using DigitalBrain.Runtime.Orleans.Synapses;
using DigitalBrain.Spi;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Runtime.Orleans.Signalling;

/// <summary>The runtime-internal contract of the listener neuron; <see cref="BrainListeners"/> is its only caller.</summary>
[NeuronAlias("runtime.listener")]
internal interface IBrainListenerHost
{
    /// <summary>Adds <paramref name="aliases"/> to the brain-wide synapse.</summary>
    Task Listen(string[] aliases);

    /// <summary>The silo this listener runs on.</summary>
    [ReadOnlyCall]
    Task<string> Silo();
}

/// <summary>
/// The per-brain listener neuron (04 D-02, R4-03) on <c>neuron.host</c>, keyed by the brain: owns the one brain-wide synapse and
/// dispatches each delivered signal to the <see cref="IBrainListeners"/> handlers of its silo. Brain-affine placement puts it with
/// the brain's neurons. With no handler on its silo for a full <see cref="SessionSynapseOptions.SessionTtl"/> it removes its
/// synapse and ends.
/// </summary>
[Neuron(BrainListeners.NeuronType)]
internal sealed class BrainListenerNeuron(IOptions<SessionSynapseOptions> options, RuntimeClock clock, BrainListeners listeners)
    : Neuron, IBrainListenerHost, ISignalSink
{
    internal const string SweepTimer = "listener.ttl";

    private readonly SessionSynapseOptions _options = options.Value;
    private DateTimeOffset? _emptySince;

    private HostNeuronContext Host => (HostNeuronContext)Context;

    protected override Task OnActivate(CancellationToken cancellationToken)
    {
        if (Id.Key != Id.Brain.Value) { throw new InvalidOperationException($"A listener neuron is keyed by its brain: {Id.Brain}/{BrainListeners.NeuronType}/{Id.Brain}."); }
        Context.Timers.Start(SweepTimer, _options.SessionSweepEvery, _options.SessionSweepEvery);
        return Task.CompletedTask;
    }

    public Task Listen(string[] aliases)
    {
        _emptySince = null;
        return Host.Engine.EnsureBrain(aliases);
    }

    public Task<string> Silo() => Task.FromResult(((IHostNeuronContext)Context).Silo);

    public Task Deliver(JournaledSignal item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return listeners.Dispatch(Id.Brain, item.Signal);
    }

    protected override async Task OnReminder(string name, CancellationToken cancellationToken)
    {
        if (name != SweepTimer) { return; }
        if (listeners.Count(Id.Brain) > 0)
        {
            _emptySince = null;
            return;
        }
        _emptySince ??= clock.Now;
        if (clock.Now - _emptySince < _options.SessionTtl) { return; }
        await Host.Engine.RemoveBrain().ConfigureAwait(true);
        Context.Timers.Stop(SweepTimer);
        Host.DeactivateOnIdle();
    }
}
