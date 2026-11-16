using Orleans.Runtime;

namespace DigitalBrain.Runtime.Orleans.Access;

/// <summary>
/// Marks async execution originating in-process on the Silo host (outside any grain turn).
/// In-process Silo callers have SourceId starting with sys.client/hosted- or null in Orleans, but hold valid cluster/kernel authority.
/// ProcessSecretToken exists only in local silo memory and can never be forged or sent by external clients over network RPC.
/// </summary>
internal static class InProcessCallerScope
{
    private const string RequestContextKey = "db.internal.inprocess_scope";
    private const string EdgeRequestContextKey = "db.internal.edge_scope";
    private static readonly object ProcessSecretToken = new();
    private static readonly object EdgeSecretToken = new();
    private static readonly AsyncLocal<bool> LocalActive = new();
    private static readonly AsyncLocal<bool> LocalEdgeActive = new();

    public static bool IsActive => LocalActive.Value || ReferenceEquals(RequestContext.Get(RequestContextKey), ProcessSecretToken);

    public static bool IsEdgeActive => IsActive || LocalEdgeActive.Value || ReferenceEquals(RequestContext.Get(EdgeRequestContextKey), EdgeSecretToken);

    public static IDisposable Enter()
    {
        var prior = RequestContext.Get(RequestContextKey);
        var priorLocal = LocalActive.Value;
        RequestContext.Set(RequestContextKey, ProcessSecretToken);
        LocalActive.Value = true;
        return new Scope(RequestContextKey, prior, LocalActive, priorLocal);
    }

    public static IDisposable EnterEdge()
    {
        var prior = RequestContext.Get(EdgeRequestContextKey);
        var priorLocal = LocalEdgeActive.Value;
        RequestContext.Set(EdgeRequestContextKey, EdgeSecretToken);
        LocalEdgeActive.Value = true;
        return new Scope(EdgeRequestContextKey, prior, LocalEdgeActive, priorLocal);
    }

    private sealed class Scope(string key, object? prior, AsyncLocal<bool> local, bool priorLocal) : IDisposable
    {
        public void Dispose()
        {
            if (prior is null)
                RequestContext.Remove(key);
            else
                RequestContext.Set(key, prior);

            local.Value = priorLocal;
        }
    }
}
