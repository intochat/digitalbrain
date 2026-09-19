using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Scenarios;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Synapses;
using Orleans.EventSourcing;
using Orleans.Providers;

namespace DigitalBrain.Core;

[GrainType("scenario")]
[LogConsistencyProvider(ProviderName = LogStorage)]
[StorageProvider(ProviderName = DigitalBrainNames.DefaultGrainStorage)]
public sealed class ScenarioGrain : JournaledGrain<ScenarioState>, IScenario
{
    public const string LogStorage = "LogStorage";

    public async Task Bind(INeuron source, string signalType, INeuron target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(signalType);
        if (State.Synapses.Any(edge =>
                ScenarioState.Same(edge.Source, source) && ScenarioState.Same(edge.Target, target)
                && string.Equals(edge.SignalType, signalType, StringComparison.Ordinal)))
        {
            return;
        }

        RaiseEvent(new SynapseBound(source, target, signalType, DateTimeOffset.UtcNow));
        await ConfirmEvents();
    }

    public async Task Unbind(INeuron source, string signalType, INeuron target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(signalType);
        if (!State.Synapses.Any(edge =>
                ScenarioState.Same(edge.Source, source) && ScenarioState.Same(edge.Target, target)
                && string.Equals(edge.SignalType, signalType, StringComparison.Ordinal)))
        {
            return;
        }

        RaiseEvent(new SynapseUnbound(source, target, signalType));
        await ConfirmEvents();
    }

    public Task<IReadOnlyList<Synapse>> Read() => Task.FromResult<IReadOnlyList<Synapse>>([.. State.Synapses]);

    public async Task<int> Route(SignalDelivery delivery, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        var accepted = 0;
        foreach (var edge in State.Synapses)
        {
            if (!ScenarioState.Same(edge.Source, delivery.Source)
                || !string.Equals(edge.SignalType, delivery.Signal.Type, StringComparison.Ordinal))
            {
                continue;
            }

            var admission = await edge.Target.HandleSignal(delivery, cancellationToken).ConfigureAwait(true);
            if (admission == SignalAdmission.Accepted)
            {
                accepted++;
            }
        }

        return accepted;
    }
}
