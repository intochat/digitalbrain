using System.Diagnostics;

namespace DigitalBrain.Capabilities.Compute;

public static class TraceContext
{
    private static readonly AsyncLocal<Trace?> Ambient = new();

    public static Trace? Current => Ambient.Value;

    public static IDisposable Enter(Trace trace)
    {
        ArgumentNullException.ThrowIfNull(trace);
        var previous = Ambient.Value;
        Ambient.Value = trace;
        return new Scope(previous);
    }

    public static NeuronId OriginOf(string brainId, string osKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(brainId);
        ArgumentException.ThrowIfNullOrWhiteSpace(osKey);
        if (osKey == $"{brainId}/edge") { return new NeuronId(new BrainId(brainId), "edge", "request"); }
        if (NeuronId.TryParse(osKey, out var parsed) && parsed.Brain.Value == brainId) { return parsed; }
        var rest = osKey.StartsWith(brainId + "/", StringComparison.Ordinal) ? osKey[(brainId.Length + 1)..] : osKey;
        var slash = rest.IndexOf('/', StringComparison.Ordinal);
        var type = slash < 0 ? rest : rest[..slash];
        var key = slash < 0 ? "request" : rest[(slash + 1)..];
        return new NeuronId(new BrainId(brainId), type, key);
    }

    public static Trace Begin(string payerAccountId, string brainId, NeuronId origin, long? budgetMicros = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payerAccountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(brainId);
        var traceId = Activity.Current?.TraceId.ToHexString() ?? Guid.NewGuid().ToString("N");
        return new()
        {
            TraceId = traceId,
            PayerAccountId = payerAccountId,
            BrainId = brainId,
            Origin = origin,
            BudgetMicros = budgetMicros,
        };
    }

    private sealed class Scope(Trace? previous) : IDisposable
    {
        public void Dispose() => Ambient.Value = previous;
    }
}
