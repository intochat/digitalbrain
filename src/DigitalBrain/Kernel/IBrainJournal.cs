
namespace DigitalBrain.Kernel;

/// <summary>The primary driven storage port for atomic state, signal, and cursor persistence.</summary>
internal interface IBrainJournal : ISignalHistory
{
    BrainId Brain { get; }

    /// <summary>Atomically commits state, signals, and cursor move as a single journal entry.</summary>
    /// <exception cref="StaleVersionException">A record changed since the writer read it; nothing is written.</exception>
    Task<CommitResult> Append(Commit commit, CancellationToken ct = default);

    /// <summary>Reads the current state record for the specified owner and state name.</summary>
    Task<JournalRecord?> ReadRecord(NeuronId owner, string name, CancellationToken ct = default);

    /// <summary>CommitAsync helper.</summary>
    Task<CommitResult> CommitAsync(BrainId brain, Commit commit, CancellationToken ct = default) => Append(commit, ct);

    /// <summary>ReadSignalsAsync helper.</summary>
    async Task<IReadOnlyList<Signal>> ReadSignalsAsync(BrainId brain, Position from, int count, CancellationToken ct = default)
    {
        var page = await Read(from.Value, count, ct).ConfigureAwait(false);
        return [.. page.Signals.Select(j => j.Signal)];
    }

    /// <summary>LoadStateAsync helper.</summary>
    async Task<(long Version, ReadOnlyMemory<byte> StateBytes)> LoadStateAsync(BrainId brain, NeuronId neuron, CancellationToken ct = default)
    {
        var record = await ReadRecord(neuron, "state", ct).ConfigureAwait(false);
        return record is null ? (0, ReadOnlyMemory<byte>.Empty) : (record.Version, record.Bytes);
    }
}

/// <summary>Driven port notifying listeners and subscribers after a successful append.</summary>
internal interface ISignalNudge
{
    /// <summary>Notifies that signals were committed at the specified brain position.</summary>
    Task Appended(NeuronId publisher, IReadOnlyList<JournaledSignal> appended, Position last, CancellationToken ct = default);

    /// <summary>Encoded path without object instances; rings doorbell based on aliases.</summary>
    void Appended(NeuronId publisher, IReadOnlyCollection<string> aliases, Position last);

    /// <summary>Offers an unjournaled ephemeral signal to cluster listeners.</summary>
    Task Offered(NeuronId publisher, Signal signal, CancellationToken ct = default);
}
