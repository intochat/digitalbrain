using System.Diagnostics;

namespace DigitalBrain.Capabilities.Compute;

public static class TraceContext
{
    public const string RequestContextKey = "intochatos.trace";

    private static readonly AsyncLocal<Trace?> Ambient = new();
    private static Func<string, object?>? s_requestContextGet;
    private static Action<string, object>? s_requestContextSet;
    private static Func<string, bool>? s_requestContextRemove;

    public static Trace? Current => RequestContextGet()?.Invoke(RequestContextKey) as Trace ?? Ambient.Value;

    public static IDisposable Enter(Trace trace)
    {
        ArgumentNullException.ThrowIfNull(trace);
        var previousAmbient = Ambient.Value;
        var previousRequest = RequestContextGet()?.Invoke(RequestContextKey);
        Ambient.Value = trace;
        RequestContextSet()?.Invoke(RequestContextKey, trace);
        return new Scope(previousAmbient, previousRequest);
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

    private static Func<string, object?>? RequestContextGet()
    {
        if (s_requestContextGet is not null) { return s_requestContextGet; }
        var method = FindRequestContextType()?.GetMethod("Get", [typeof(string)]);
        if (method is not null)
        {
            s_requestContextGet = method.CreateDelegate<Func<string, object?>>();
            return s_requestContextGet;
        }
        return null;
    }

    private static Action<string, object>? RequestContextSet()
    {
        if (s_requestContextSet is not null) { return s_requestContextSet; }
        var method = FindRequestContextType()?.GetMethod("Set", [typeof(string), typeof(object)]);
        if (method is not null)
        {
            s_requestContextSet = method.CreateDelegate<Action<string, object>>();
            return s_requestContextSet;
        }
        return null;
    }

    private static Func<string, bool>? RequestContextRemove()
    {
        if (s_requestContextRemove is not null) { return s_requestContextRemove; }
        var method = FindRequestContextType()?.GetMethod("Remove", [typeof(string)]);
        if (method is not null)
        {
            s_requestContextRemove = method.CreateDelegate<Func<string, bool>>();
            return s_requestContextRemove;
        }
        return null;
    }

    private static Type? FindRequestContextType()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (string.Equals(assembly.GetName().Name, "Orleans.Core.Abstractions", StringComparison.Ordinal))
            {
                return assembly.GetType("Orleans.Runtime.RequestContext");
            }
        }
        return null;
    }

    private sealed class Scope(Trace? previousAmbient, object? previousRequest) : IDisposable
    {
        public void Dispose()
        {
            Ambient.Value = previousAmbient;
            if (previousRequest is null)
            {
                _ = RequestContextRemove()?.Invoke(RequestContextKey);
            }
            else
            {
                RequestContextSet()?.Invoke(RequestContextKey, previousRequest);
            }
        }
    }
}
