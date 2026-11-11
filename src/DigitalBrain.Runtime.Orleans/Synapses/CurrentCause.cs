using DigitalBrain.Kernel;
using DigitalBrain.Neurons;
using DigitalBrain.Capabilities.Compute;
using Orleans.Runtime;

namespace DigitalBrain.Runtime.Orleans.Synapses;

/// <summary>
/// The journaled signal a running reaction or <c>[On]</c> handler reacts to. The engine opens it for the duration of the handler; every
/// save or publish in that flow journals its signals with the cause's trace id and the cause's <see cref="Causation.Address"/>. The
/// trace id and address also ride the Orleans request context, so a neuron the handler calls stamps its saves the same way. Outside
/// a handler a save takes the request's <see cref="TraceContext"/> trace id, and with neither a fresh one.
/// </summary>
internal static class CurrentCause
{
    /// <summary>Request-context key of the cause's trace id.</summary>
    public const string TraceKey = "intochatos.cause.trace";

    /// <summary>Request-context key of the cause's <see cref="Causation.Address"/>.</summary>
    public const string CausationKey = "intochatos.cause.id";

    private static readonly AsyncLocal<(string TraceId, string CausationId)?> Current = new();

    // The fresh trace id minted for this async flow when it had no cause and no request trace: every later stamp of the flow (a
    // flow's State.TraceId and the save that journals FlowStarted) agrees on it. Set inside a turn's async method, so it never
    // reaches the caller's flow or the next turn.
    private static readonly AsyncLocal<string?> Fresh = new();

    /// <summary>Makes <paramref name="cause"/> the current cause of this flow; disposing the scope restores the previous one.</summary>
    public static Scope Open(JournaledSignal cause)
    {
        ArgumentNullException.ThrowIfNull(cause);
        var previous = Current.Value;
        var previousTrace = RequestContext.Get(TraceKey);
        var previousCausation = RequestContext.Get(CausationKey);
        var traceId = cause.TraceId ?? Inherited().TraceId;
        var causationId = Causation.Address(cause.Publisher, cause.Sequence);
        Current.Value = (traceId, causationId);
        RequestContext.Set(TraceKey, traceId);
        RequestContext.Set(CausationKey, causationId);
        return new Scope(previous, previousTrace, previousCausation);
    }

    /// <summary>The trace and causation ids the signals of a save made now are journaled with.</summary>
    public static (string TraceId, string? CausationId) Stamp()
        => Current.Value is { } cause ? cause : Inherited();

    // A cause handed over by a calling reaction, else the request's trace, else a fresh one.
    private static (string TraceId, string? CausationId) Inherited()
        => RequestContext.Get(TraceKey) is string trace
            ? (trace, RequestContext.Get(CausationKey) as string)
            : (TraceContext.Current?.TraceId ?? (Fresh.Value ??= Guid.NewGuid().ToString("N")), null);

    /// <summary>Restores the previous cause when disposed.</summary>
    public readonly struct Scope((string, string)? previous, object? previousTrace, object? previousCausation) : IDisposable
    {
        public void Dispose()
        {
            Current.Value = previous;
            Restore(TraceKey, previousTrace);
            Restore(CausationKey, previousCausation);
        }

        private static void Restore(string key, object? value)
        {
            if (value is null) { RequestContext.Remove(key); }
            else { RequestContext.Set(key, value); }
        }
    }
}
