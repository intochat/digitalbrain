namespace DigitalBrain.Kernel;

/// <summary>One record write inside an entry.</summary>
internal sealed record RecordWrite(string Name, StateChange Change, ReadOnlyMemory<byte> Bytes, long? Expected, string? Type = null);

/// <summary>Stored record inside the journal. Version: the sequence of the entry that last wrote it.</summary>
internal sealed record JournalRecord(ReadOnlyMemory<byte> Bytes, long Version, string? Type = null);

/// <summary>Publisher sequence range.</summary>
internal readonly record struct PublisherRange(long Floor, long Last, long LastPosition = 0);

/// <summary>Trimmed signal range metadata.</summary>
internal readonly record struct TrimmedRange(string Publisher, long FirstPosition, long LastPosition, long FirstSignal, long LastSignal);

/// <summary>Decided entry ready for apply or persistence.</summary>
internal sealed record JournalEntry<TSignal>(
    long Sequence,
    string Owner,
    RecordWrite? State,
    RecordWrite? Side,
    string Publisher,
    IReadOnlyList<TSignal> Signals,
    long FirstSignal,
    long FirstPosition,
    long AtUnixMs);

/// <summary>One brain's journal as a pure aggregate. Pure domain logic: zero IO, zero clock, zero locks.</summary>
internal sealed class Journal<TSignal>
{
    private readonly Dictionary<string, Dictionary<string, JournalRecord>> _records = new(StringComparer.Ordinal);
    private readonly List<JournalEntry<TSignal>> _held = [];
    private readonly List<TrimmedRange> _trimmed = [];
    private readonly Dictionary<string, PublisherRange> _marks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _floors = new(StringComparer.Ordinal);

    public long Sequence { get; private set; }
    public long Position { get; private set; }

    public JournalRecord? Record(string owner, string name) => _records.GetValueOrDefault(owner)?.GetValueOrDefault(name);

    public long FloorOf(string publisher) => _floors.GetValueOrDefault(publisher, 0);
    public long LastOf(string publisher) => _marks.GetValueOrDefault(publisher).Last;
    public long LastPositionOf(string publisher) => _marks.GetValueOrDefault(publisher).LastPosition;
    public IReadOnlyDictionary<string, PublisherRange> Marks => _marks;

    public List<(string Owner, JournalRecord Record)> Named(string name)
    {
        var found = new List<(string, JournalRecord)>();
        foreach (var (owner, records) in _records)
        {
            if (records.TryGetValue(name, out var record) && !record.Bytes.IsEmpty)
            {
                found.Add((owner, record));
            }
        }
        return found;
    }

    /// <summary>Checks every expected version and numbers the entry. Does not mutate aggregate state.</summary>
    public JournalEntry<TSignal> Decide(string owner, RecordWrite? state, RecordWrite? side, string publisher, IReadOnlyList<TSignal> signals, long atUnixMs)
    {
        ArgumentNullException.ThrowIfNull(signals);
        if (signals.Count > 0) { ArgumentException.ThrowIfNullOrEmpty(publisher); }
        if (state is not null && side is not null && side.Name == state.Name)
        {
            throw new ArgumentException("The cursor record must be another record of the owner.", nameof(side));
        }
        Check(owner, state);
        Check(owner, side);
        var last = signals.Count > 0 ? _marks.GetValueOrDefault(publisher).Last : 0;
        return new JournalEntry<TSignal>(
            checked(Sequence + 1),
            owner,
            state,
            side,
            signals.Count > 0 ? publisher : "",
            signals,
            checked(last + 1),
            checked(Position + 1),
            atUnixMs);
    }

    /// <summary>Folds a decided (or replayed) entry. Enforces strict contiguity.</summary>
    public void Apply(JournalEntry<TSignal> entry)
    {
        if (entry.Sequence != Sequence + 1 || entry.FirstPosition != Position + 1
            || (entry.Signals.Count > 0 && entry.FirstSignal != _marks.GetValueOrDefault(entry.Publisher).Last + 1))
        {
            throw new InvalidDataException("The brain journal is not contiguous.");
        }
        Fold(entry.Owner, entry.State, entry.Sequence);
        Fold(entry.Owner, entry.Side, entry.Sequence);
        if (entry.Signals.Count > 0)
        {
            _held.Add(entry with { Owner = "", State = null, Side = null });
            var lastPos = entry.FirstPosition + entry.Signals.Count - 1;
            var range = _marks.GetValueOrDefault(entry.Publisher);
            _marks[entry.Publisher] = new PublisherRange(range.Floor, entry.FirstSignal + entry.Signals.Count - 1, lastPos);
            Position = lastPos;
        }
        Sequence = entry.Sequence;
    }

    private void Check(string owner, RecordWrite? write)
    {
        if (write is null) { return; }
        var stored = Record(owner, write.Name)?.Version;
        if (stored != write.Expected) { throw new StaleVersionException(owner, write.Name, stored, write.Expected); }
    }

    private void Fold(string owner, RecordWrite? write, long sequence)
    {
        if (write is null) { return; }
        if (write.Change == StateChange.Clear)
        {
            _records.GetValueOrDefault(owner)?.Remove(write.Name);
            return;
        }
        if (!_records.TryGetValue(owner, out var named)) { _records[owner] = named = new(StringComparer.Ordinal); }
        named[write.Name] = new JournalRecord(write.Bytes, sequence, write.Type);
    }

    public IEnumerable<JournalEntry<TSignal>> HeldAfter(long position, string? publisher, int limit)
    {
        var query = _held.Where(e => e.FirstPosition + e.Signals.Count - 1 > position);
        if (!string.IsNullOrEmpty(publisher))
        {
            query = query.Where(e => string.Equals(e.Publisher, publisher, StringComparison.Ordinal));
        }
        return query.Take(limit);
    }

    public void Trim(string publisher, long floorSequence, long floorPosition)
    {
        _floors[publisher] = Math.Max(_floors.GetValueOrDefault(publisher), floorPosition);
        _marks[publisher] = _marks.GetValueOrDefault(publisher) with { Floor = floorSequence };
        for (var index = _held.Count - 1; index >= 0; index--)
        {
            var entry = _held[index];
            if (!string.Equals(entry.Publisher, publisher, StringComparison.Ordinal) || entry.FirstSignal > floorSequence) { continue; }
            var drop = (int)Math.Min(entry.Signals.Count, floorSequence - entry.FirstSignal + 1);
            _trimmed.Add(new TrimmedRange(entry.Publisher, entry.FirstPosition, entry.FirstPosition + drop - 1, entry.FirstSignal, entry.FirstSignal + drop - 1));
            if (drop == entry.Signals.Count) { _held.RemoveAt(index); continue; }
            _held[index] = entry with
            {
                Signals = [.. entry.Signals.Skip(drop)],
                FirstSignal = entry.FirstSignal + drop,
                FirstPosition = entry.FirstPosition + drop
            };
        }
    }

    public long SequenceAt(string publisher, long position)
    {
        if (position <= 0) { return 0; }
        foreach (var entry in _held)
        {
            if (entry.Publisher == publisher)
            {
                if (position >= entry.FirstPosition && position < entry.FirstPosition + entry.Signals.Count)
                {
                    return entry.FirstSignal + (position - entry.FirstPosition);
                }
            }
        }
        foreach (var trimmed in _trimmed)
        {
            if (trimmed.Publisher == publisher)
            {
                if (position >= trimmed.FirstPosition && position <= trimmed.LastPosition)
                {
                    return trimmed.FirstSignal + (position - trimmed.FirstPosition);
                }
            }
        }

        long lastSeq = 0;
        long lastPos = 0;
        foreach (var entry in _held)
        {
            if (entry.Publisher == publisher && entry.FirstPosition <= position && entry.FirstPosition > lastPos)
            {
                lastPos = entry.FirstPosition;
                lastSeq = entry.FirstSignal + entry.Signals.Count - 1;
            }
        }
        foreach (var trimmed in _trimmed)
        {
            if (trimmed.Publisher == publisher && trimmed.FirstPosition <= position && trimmed.FirstPosition > lastPos)
            {
                lastPos = trimmed.FirstPosition;
                lastSeq = trimmed.LastSignal;
            }
        }
        if (lastSeq > 0) { return lastSeq; }

        if (_marks.TryGetValue(publisher, out var mark) && mark.Floor > 0 && position <= _floors.GetValueOrDefault(publisher))
        {
            return mark.Floor;
        }
        return 0;
    }

    public long PositionAt(string publisher, long sequence)
    {
        if (sequence <= 0) { return 0; }
        foreach (var entry in _held)
        {
            if (entry.Publisher == publisher)
            {
                if (sequence >= entry.FirstSignal && sequence < entry.FirstSignal + entry.Signals.Count)
                {
                    return entry.FirstPosition + (sequence - entry.FirstSignal);
                }
            }
        }
        foreach (var trimmed in _trimmed)
        {
            if (trimmed.Publisher == publisher)
            {
                if (sequence >= trimmed.FirstSignal && sequence <= trimmed.LastSignal)
                {
                    return trimmed.FirstPosition + (sequence - trimmed.FirstSignal);
                }
            }
        }
        return 0;
    }

    public IReadOnlyList<JournalEntry<TSignal>> HeldEntries => _held;

    public Dictionary<string, Dictionary<string, JournalRecord>> SnapshotRecords()
    {
        var copy = new Dictionary<string, Dictionary<string, JournalRecord>>(StringComparer.Ordinal);
        foreach (var (owner, dict) in _records)
        {
            copy[owner] = new Dictionary<string, JournalRecord>(dict, StringComparer.Ordinal);
        }
        return copy;
    }

    public Dictionary<string, PublisherRange> SnapshotMarks() => new(_marks, StringComparer.Ordinal);

    public void Restore(long sequence, long position, Dictionary<string, Dictionary<string, JournalRecord>> records, Dictionary<string, PublisherRange> marks, IEnumerable<JournalEntry<TSignal>> held)
    {
        Sequence = sequence;
        Position = position;
        _records.Clear();
        foreach (var (owner, dict) in records)
        {
            _records[owner] = new Dictionary<string, JournalRecord>(dict, StringComparer.Ordinal);
        }
        _marks.Clear();
        foreach (var (pub, range) in marks)
        {
            _marks[pub] = range;
        }
        _held.Clear();
        _held.AddRange(held);
        _trimmed.Clear();
    }
}
