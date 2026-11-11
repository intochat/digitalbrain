namespace DigitalBrain.Kernel;

/// <summary>Single ambient reaction scope replacing ReactionCursor and TestSynapses.Reacting.</summary>
internal sealed class ReactionScope : IDisposable
{
    private static readonly AsyncLocal<ReactionScope?> Current = new();
    private readonly NeuronId _subscriber;
    private readonly Func<long?, CursorMove?> _move;
    private readonly Action? _carried;
    private readonly ReactionScope? _outer;
    private bool _closed;

    public long? Version { get; private set; }
    public bool Carried { get; private set; }

    private ReactionScope(NeuronId subscriber, Func<long?, CursorMove?> move, long? version, Action? carried, ReactionScope? outer)
    {
        _subscriber = subscriber;
        _move = move;
        Version = version;
        _carried = carried;
        _outer = outer;
    }

    public CursorMove? Move() => _move(Version);

    public static ReactionScope Open(NeuronId subscriber, Func<long?, CursorMove?> move, long? version, Action? carried = null)
    {
        var scope = new ReactionScope(subscriber, move, version, carried, Current.Value);
        Current.Value = scope;
        return scope;
    }

    public static ReactionScope? Of(NeuronId writer)
    {
        var scope = Current.Value;
        while (scope is { _closed: true }) { scope = scope._outer; }
        return scope is not null && scope._subscriber == writer ? scope : null;
    }

    public void Carry(long version)
    {
        Version = version;
        Carried = true;
        _carried?.Invoke();
    }

    public void Dispose()
    {
        _closed = true;
        if (Carried && Version is { } version && _outer is { _closed: false } outer && outer._subscriber == _subscriber)
        {
            outer.Version = version;
            outer.Carried = true;
        }
        Current.Value = _outer;
    }
}
