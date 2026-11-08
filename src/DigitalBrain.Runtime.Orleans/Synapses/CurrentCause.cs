using DigitalBrain.Capabilities.Compute;

namespace DigitalBrain.Runtime.Orleans.Synapses;

/// <summary>
/// The journaled signal a running reaction or <c>[On]</c> handler reacts to. The engine opens it for the duration of the handler; every
/// save or publish in that flow journals its signals with the cause's trace id and the cause's <see cref="Causation.Address"/>.
/// Outside a handler a save takes the request's <see cref="TraceContext"/> trace id, and with neither a fresh one.
/// </summary>
internal static class CurrentCause
{
    private static readonly AsyncLocal<JournaledSignal?> Current = new();

    /// <summary>Makes <paramref name="cause"/> the current cause of this flow; disposing the scope restores the previous one.</summary>
    public static Scope Open(JournaledSignal cause)
    {
        ArgumentNullException.ThrowIfNull(cause);
        var previous = Current.Value;
        Current.Value = cause;
        return new Scope(previous);
    }

    /// <summary>The trace and causation ids the signals of a save made now are journaled with.</summary>
    public static (string TraceId, string? CausationId) Stamp()
        => Current.Value is { } cause
            ? (cause.TraceId ?? TraceContext.Current?.TraceId ?? Guid.NewGuid().ToString("N"), Causation.Address(cause.Publisher, cause.Sequence))
            : (TraceContext.Current?.TraceId ?? Guid.NewGuid().ToString("N"), null);

    /// <summary>Restores the previous cause when disposed.</summary>
    public readonly struct Scope(JournaledSignal? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}
