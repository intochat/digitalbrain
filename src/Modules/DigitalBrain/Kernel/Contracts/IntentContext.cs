namespace DigitalBrain.Contracts;

/// <summary>
/// One meterable provider-call entry. The concrete token-usage shape is owned by the module that
/// calls the provider; the kernel only carries entries so the intent scope can batch them.
/// </summary>
public interface IIntentUsageEntry;

/// <summary>
/// The ambient identity of the user request being served. Stamped only at a trusted edge
/// (the HTTP endpoint that starts an intent) and read by metering decorators so usage is
/// keyed by intent id rather than by whichever grain happens to make the call. The endpoint
/// also owns the intent's usage batch here, so all provider calls in the intent accumulate in
/// one place and are flushed once by the batch sink when the intent completes.
/// </summary>
public sealed record IntentContext(string IntentId, string? ScopeId = null) : IDisposable
{
    private static readonly AsyncLocal<IntentContext?> Ambient = new();
    private readonly List<IIntentUsageEntry> _usage = [];
    private IntentContext? _previous;
    private bool _disposed;

    public static IntentContext? Current => Ambient.Value;

    /// <summary>The provider-call entries recorded so far in this intent.</summary>
    public IReadOnlyList<IIntentUsageEntry> Usage => _usage;

    public void AddUsage(IIntentUsageEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _usage.Add(entry);
    }

    public static IntentContext Begin(string intentId, string? scopeId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(intentId);
        var context = new IntentContext(intentId, scopeId) { _previous = Ambient.Value };
        Ambient.Value = context;
        return context;
    }

    public void Dispose()
    {
        if (_disposed) { return; }
        _disposed = true;
        Ambient.Value = _previous;
    }
}