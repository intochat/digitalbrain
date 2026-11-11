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
    private static readonly object ProcessSecretToken = new();
    private static readonly AsyncLocal<bool> LocalActive = new();

    public static bool IsActive => LocalActive.Value || ReferenceEquals(RequestContext.Get(RequestContextKey), ProcessSecretToken);

    public static IDisposable Enter()
    {
        var prior = RequestContext.Get(RequestContextKey);
        var priorLocal = LocalActive.Value;
        RequestContext.Set(RequestContextKey, ProcessSecretToken);
        LocalActive.Value = true;
        return new Scope(prior, priorLocal);
    }

    private sealed class Scope(object? prior, bool priorLocal) : IDisposable
    {
        public void Dispose()
        {
            if (prior is null)
                RequestContext.Remove(RequestContextKey);
            else
                RequestContext.Set(RequestContextKey, prior);

            LocalActive.Value = priorLocal;
        }
    }
}
