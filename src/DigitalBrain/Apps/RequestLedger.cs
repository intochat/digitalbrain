namespace DigitalBrain;

/// <summary>
/// The request ids an app has handled, the newest <see cref="Capacity"/> kept, so a request delivered twice (a retained request read again
/// by a fresh subscription) runs once. <see cref="Ids"/> and the restoring constructor let an app keep the ledger in its state.
/// </summary>
public sealed class RequestLedger
{
    /// <summary>The default number of ids kept.</summary>
    public const int DefaultCapacity = 256;

    private readonly Lock _gate = new();
    private readonly List<Guid> _ids = [];
    private readonly HashSet<Guid> _set = [];

    /// <summary>An empty ledger keeping <see cref="DefaultCapacity"/> ids.</summary>
    public RequestLedger() : this([], DefaultCapacity)
    {
    }

    /// <summary>A ledger restored from <paramref name="ids"/> (oldest first), keeping <paramref name="capacity"/> ids.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is below 1.</exception>
    public RequestLedger(IEnumerable<Guid> ids, int capacity = DefaultCapacity)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        Capacity = capacity;
        foreach (var id in ids) { Record(id); }
    }

    /// <summary>How many ids the ledger keeps.</summary>
    public int Capacity { get; }

    /// <summary>The kept ids, oldest first.</summary>
    public IReadOnlyList<Guid> Ids
    {
        get
        {
            lock (_gate) { return [.. _ids]; }
        }
    }

    /// <summary>True when <paramref name="id"/> was handled.</summary>
    public bool Handled(Guid id)
    {
        lock (_gate) { return _set.Contains(id); }
    }

    /// <summary>Records <paramref name="id"/> as handled.</summary>
    /// <returns>False when it was already handled: the caller skips the request.</returns>
    public bool Record(Guid id)
    {
        lock (_gate)
        {
            if (!_set.Add(id)) { return false; }
            _ids.Add(id);
            if (_ids.Count > Capacity)
            {
                _set.Remove(_ids[0]);
                _ids.RemoveAt(0);
            }
            return true;
        }
    }
}
