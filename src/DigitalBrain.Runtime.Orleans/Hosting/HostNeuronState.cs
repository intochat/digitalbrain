using DigitalBrain.Runtime.Orleans.Synapses;
using DigitalBrain.Spi;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Runtime;
using Orleans.Storage;

namespace DigitalBrain.Runtime.Orleans.Hosting;

/// <summary>
/// <see cref="INeuronState"/> of a hosted neuron over the <c>cluster</c> grain store (BrainLog, JSON payloads, 06 D-14). A save
/// writes the state and its signals as one journal entry under the class state name, and only after that append returns are the
/// <see cref="AppendedSignals"/> handed to <see cref="SignalPublisher"/> (04 D-01).
/// </summary>
internal sealed class HostNeuronState(NeuronId id, GrainId grainId, NeuronRegistration registration, IServiceProvider services) : INeuronState
{
    private readonly HashSet<string> _ensured = new(StringComparer.Ordinal);
    private string? _etag;

    private IGrainStorage Store => services.GetKeyedService<IGrainStorage>(RuntimeOptions.From(services).ClusterGrainStorage)
        ?? throw new InvalidOperationException($"{id} has state, but the silo has no '{RuntimeOptions.From(services).ClusterGrainStorage}' grain store.");

    public async Task<T?> Load<T>()
    {
        if (registration.Policy == HostPolicy.Stateless) { return default; }
        var state = new GrainState<T>();
        await Store.ReadStateAsync(registration.StateName, grainId, state).ConfigureAwait(true);
        _etag = state.ETag;
        return state.RecordExists ? state.State : default;
    }

    /// <exception cref="InvalidOperationException">The class is a <see cref="StatelessNeuronAttribute"/> class, or the <c>cluster</c> store is not BrainLog.</exception>
    public async Task Save<T>(T next, IReadOnlyList<Signal> signals)
    {
        ArgumentNullException.ThrowIfNull(signals);
        RefuseStateless();
        var state = new GrainState<T>(next) { ETag = _etag };
        if (Store is not BrainGrainStorage journal)
        {
            if (signals.Count > 0) { throw new InvalidOperationException($"{id} saves signals with its state, which needs BrainLog as the cluster store (BrainStorageRegistration)."); }
            await Store.WriteStateAsync(registration.StateName, grainId, state).ConfigureAwait(true);
            _etag = state.ETag;
            return;
        }
        // Gates run first, as nested turns of the gate neurons; a refusal throws before anything is written.
        var allowed = await GateRunner.Run(services, id, signals, _ensured, CancellationToken.None).ConfigureAwait(true);
        // A save inside a synapse reaction carries the subscriber's advanced cursor in the same entry (R4-09).
        long last;
        try { last = await journal.WriteStateAsync(registration.StateName, grainId, state, id.ToString(), signals, ReactionCursor.For(grainId)).ConfigureAwait(true); }
        catch
        {
            await GateRunner.Abort(services, allowed).ConfigureAwait(true);
            throw;
        }
        _etag = state.ETag;
        await Announce(last, signals).ConfigureAwait(true);
        await GateRunner.Commit(services, id, allowed).ConfigureAwait(true);
    }

    public async Task Clear()
    {
        RefuseStateless();
        var state = new GrainState<object> { ETag = _etag };
        await Store.ClearStateAsync(registration.StateName, grainId, state).ConfigureAwait(true);
        _etag = null;
    }

    private static long _ephemeralSequence;

    private static bool IsEphemeral(Signal signal) =>
        signal.GetType().IsDefined(typeof(EphemeralSignalAttribute), inherit: false);

    /// <summary>Publishes an ephemeral signal directly without persisting to the journal.</summary>
    public Task PublishEphemeral(Signal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        return AnnounceEphemeral(signal);
    }

    /// <summary>Appends <paramref name="signal"/> as a signal-only entry, then publishes it.</summary>
    /// <exception cref="InvalidOperationException">The <c>cluster</c> store is not BrainLog.</exception>
    public async Task Publish(Signal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        if (IsEphemeral(signal))
        {
            await AnnounceEphemeral(signal).ConfigureAwait(true);
            return;
        }
        if (Store is not BrainGrainStorage journal) { throw new InvalidOperationException($"{id} publishes, which needs BrainLog as the cluster store (BrainStorageRegistration)."); }
        IReadOnlyList<Signal> signals = [signal];
        var allowed = await GateRunner.Run(services, id, signals, _ensured, CancellationToken.None).ConfigureAwait(true);
        long last;
        try { last = await journal.AppendSignalsAsync(registration.StateName, grainId, id.ToString(), signals, ReactionCursor.For(grainId)).ConfigureAwait(true); }
        catch
        {
            await GateRunner.Abort(services, allowed).ConfigureAwait(true);
            throw;
        }
        await Announce(last, signals).ConfigureAwait(true);
        await GateRunner.Commit(services, id, allowed).ConfigureAwait(true);
    }

    private async Task AnnounceEphemeral(Signal signal)
    {
        IReadOnlyList<Signal> signals = [signal];
        services.GetService<Signalling.ClusterListeners>()?.Offer(id, signals);
        if (services.GetService<JournalPayloads>() is { } payloads
            && payloads.Types.TryAliasOf(signal.GetType(), out var alias)
            && services.GetService<SignalPublisher>() is { } publisher)
        {
            var seq = Interlocked.Increment(ref _ephemeralSequence);
            await publisher.Publish(new AppendedSignals(id, [(seq, alias)])).ConfigureAwait(true);
        }
    }

    // Runs only after the append returned: AppendedSignals are never built for an entry that is not durable.
    private Task Announce(long last, IReadOnlyList<Signal> signals)
    {
        if (signals.Count == 0) { return Task.CompletedTask; }
        var types = services.GetRequiredService<JournalPayloads>().Types;
        var entries = signals.Select((signal, index) => (
            Sequence: last - signals.Count + 1 + index,
            Alias: types.TryAliasOf(signal.GetType(), out var alias) ? alias : throw new JournalPayloadException($"{signal.GetType().Name} has no registered [Signal] alias.")))
            .ToArray();
        services.GetService<Signalling.ClusterListeners>()?.Offer(id, signals);
        return services.GetRequiredService<SignalPublisher>().Publish(new AppendedSignals(id, entries));
    }

    private void RefuseStateless()
    {
        if (registration.Policy == HostPolicy.Stateless)
        {
            throw new InvalidOperationException($"{registration.Class.FullName} is a [StatelessNeuron] class: it has no state to save; publish with Publish instead.");
        }
    }
}
