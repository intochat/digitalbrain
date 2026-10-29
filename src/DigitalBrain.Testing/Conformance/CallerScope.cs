using DigitalBrain.Capabilities;

namespace DigitalBrain.Testing.Conformance;

/// <summary>
/// Switches the caller the subject sees. secrets@1 and identity@1 take no caller parameter: the implementation reads the
/// caller the runtime stamped on the call. A kit subclass implements <see cref="As"/> by stamping <paramref name="caller"/>
/// the way its runtime does, until the returned scope is disposed.
/// </summary>
public abstract class CallerScope
{
    /// <summary>Makes subsequent calls on this async flow run as <paramref name="caller"/> until the result is disposed.</summary>
    public abstract IDisposable As(CapabilityCaller caller);
}

/// <summary>An <see cref="AsyncLocal{T}"/> caller scope for in-memory subjects and fakes.</summary>
/// <param name="fallback">The caller seen when no scope is active; a local owner by default.</param>
public sealed class AmbientCallerScope(CapabilityCaller? fallback = null) : CallerScope
{
    /// <summary>The caller used when nothing is stamped: principal, account and brain <c>owner</c>.</summary>
    public static readonly CapabilityCaller LocalOwner = new("owner", "owner", "owner");

    private readonly AsyncLocal<CapabilityCaller?> _current = new();

    /// <summary>The caller of the current async flow.</summary>
    public CapabilityCaller Current => _current.Value ?? fallback ?? LocalOwner;

    /// <inheritdoc />
    public override IDisposable As(CapabilityCaller caller)
    {
        ArgumentNullException.ThrowIfNull(caller);
        var previous = _current.Value;
        _current.Value = caller;
        return new Restore(this, previous);
    }

    private sealed class Restore(AmbientCallerScope scope, CapabilityCaller? previous) : IDisposable
    {
        public void Dispose() => scope._current.Value = previous;
    }
}
