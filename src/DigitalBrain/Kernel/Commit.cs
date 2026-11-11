namespace DigitalBrain.Kernel;

/// <summary>Everything one journal entry of a neuron contains; durable as a whole or not at all.</summary>
internal sealed record Commit(NeuronId Publisher, StateWrite? State, IReadOnlyList<Signal> Signals, CursorMove? Cursor, Stamp Stamp)
{
    public NeuronId Neuron => Publisher;
    public bool IsEmpty => State is null && Cursor is null && Signals.Count == 0;
}

/// <summary>Version is the entry sequence. FirstSequence and FirstPosition are derived directly from the atomic append.</summary>
internal sealed record CommitResult(long Version, long FirstSequence, Position FirstPosition, DateTimeOffset At)
{
    public long NewVersion => Version;
    public Position Position => FirstPosition;

    public static CommitResult Of<TSignal>(JournalEntry<TSignal> entry)
        => new(entry.Sequence, entry.FirstSignal, new Position(entry.FirstPosition), DateTimeOffset.FromUnixTimeMilliseconds(entry.AtUnixMs));

    public IReadOnlyList<JournaledSignal> Journaled(Commit commit)
        => [.. commit.Signals.Select((signal, index)
            => new JournaledSignal(commit.Publisher, FirstSequence + index, signal, At, commit.Stamp.TraceId, commit.Stamp.CausationId))];
}

/// <summary>Exception thrown when optimistic concurrency version check fails.</summary>
internal sealed class StaleVersionException(string owner, string name, long? stored, long? expected)
    : Exception($"Record '{owner}/{name}' changed since it was read. Stored: {stored?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none"}, expected: {expected?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none"}.")
{
    public string Owner => owner;
    public string Name => name;
    public long? Stored => stored;
    public long? Expected => expected;
}
