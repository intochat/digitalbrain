using System.Text.Json;
using DigitalBrain.Invocation;
using DigitalBrain.Spi;

namespace DigitalBrain.Kernel;

/// <summary>The single write pipeline of a hosted neuron across all runtimes.</summary>
internal sealed class KernelState(
    NeuronId id,
    string stateName,
    bool stateless,
    IBrainJournal journal,
    ISignalNudge nudge,
    Func<Stamp> stamp,
    Action drop) : INeuronState
{
    private long? _version;

    public async Task<T?> Load<T>()
    {
        if (stateless) { return default; }
        var record = await journal.ReadRecord(id, stateName, CancellationToken.None).ConfigureAwait(true);
        _version = record?.Version;
        return record is null ? default : JsonSerializer.Deserialize<T>(record.Bytes.Span, RuntimeProtocol.OptionsFor(typeof(T)));
    }

    public Task Save<T>(T next, IReadOnlyList<Signal> signals)
    {
        ArgumentNullException.ThrowIfNull(signals);
        return CommitEntry(Write(next), signals);
    }

    public Task Store<T>(T next) => CommitEntry(Write(next), []);

    public Task Clear()
    {
        RefuseStateless();
        return CommitEntry(new StateWrite(stateName, StateChange.Clear, default, _version), []);
    }

    public Task Publish(Signal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        return signal.GetType().IsDefined(typeof(EphemeralSignalAttribute), inherit: false)
            ? PublishEphemeral(signal)
            : CommitEntry(null, [signal]);
    }

    public Task PublishEphemeral(Signal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        return nudge.Offered(id, signal, CancellationToken.None);
    }

    private async Task CommitEntry(StateWrite? state, IReadOnlyList<Signal> signals)
    {
        var reaction = ReactionScope.Of(id);
        var commit = new Commit(id, state, signals, reaction?.Move(), stamp());
        CommitResult result;
        try
        {
            result = await journal.Append(commit, CancellationToken.None).ConfigureAwait(true);
        }
        catch (StaleVersionException)
        {
            drop();
            throw;
        }

        if (state is not null)
        {
            _version = state.Change == StateChange.Write ? result.Version : null;
        }
        reaction?.Carry(result.Version);

        if (signals.Count > 0)
        {
            var lastPos = new Position(result.FirstPosition.Value + signals.Count - 1);
            await nudge.Appended(id, result.Journaled(commit), lastPos, CancellationToken.None).ConfigureAwait(true);
        }
    }

    private StateWrite Write<T>(T next)
    {
        RefuseStateless();
        return new StateWrite(stateName, StateChange.Write,
            JsonSerializer.SerializeToUtf8Bytes(next, RuntimeProtocol.OptionsFor(next?.GetType() ?? typeof(T))), _version);
    }

    private void RefuseStateless()
    {
        if (stateless)
        {
            throw new InvalidOperationException($"{id.Type} is a [StatelessNeuron] class: it has no state to save; publish with Publish instead.");
        }
    }
}
