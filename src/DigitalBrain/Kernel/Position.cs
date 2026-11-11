namespace DigitalBrain.Kernel;

/// <summary>Nature of a state change in an atomic entry.</summary>
internal enum StateChange
{
    Write = 0,
    Clear = 1
}

/// <summary>Distributed trace and causation stamp for an entry.</summary>
internal readonly record struct Stamp(string? TraceId, string? CausationId);

/// <summary>Unified monotonically increasing brain-wide sequence position.</summary>
internal readonly record struct Position(long Value) : IComparable<Position>
{
    public int CompareTo(Position other) => Value.CompareTo(other.Value);
    public static bool operator <(Position left, Position right) => left.Value < right.Value;
    public static bool operator >(Position left, Position right) => left.Value > right.Value;
    public static bool operator <=(Position left, Position right) => left.Value <= right.Value;
    public static bool operator >=(Position left, Position right) => left.Value >= right.Value;
    public static Position Zero => new(0);

    public static implicit operator Position(long value) => new(value);
    public static implicit operator long(Position position) => position.Value;

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>One state write operation within an atomic commit.</summary>
internal sealed record StateWrite(string Name, StateChange Change, ReadOnlyMemory<byte> Json, long? Expected)
{
    public RecordWrite ToRecord() => new(Name, Change, Json, Expected);
}

/// <summary>The subscriber's subscription record update, carrying the advanced cursor.</summary>
internal sealed record CursorMove(string Name, ReadOnlyMemory<byte> Json, long? Expected)
{
    public CursorMove(NeuronId Subscriber, long Sequence)
        : this(Subscriptions.RecordName, Subscriptions.None(Subscriber).Advance(new Subscription(null, System.Collections.Immutable.ImmutableSortedSet<string>.Empty, new Position(Sequence), DateTimeOffset.UtcNow), new Position(Sequence), DateTimeOffset.UtcNow).Encode(), null)
    {
    }

    public RecordWrite ToRecord() => new(Name, StateChange.Write, Json, Expected);
}
