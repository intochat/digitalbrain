using DigitalBrain.Contracts;
namespace DigitalBrain.Kernel;

public sealed class IntentContext : IDisposable
{
    private static readonly AsyncLocal<IntentContext?> Ambient = new();
    private readonly Lock _gate = new();
    private readonly List<IIntentUsageEntry> _usage = [];
    private readonly IntentContext? _previous;
    private bool _disposed;
    private bool _ownsAmbient;
    private IntentContext(string intentId, string? scopeId)
    { IntentId = intentId; ScopeId = scopeId; _previous = Ambient.Value; }
    public string IntentId { get; }
    public string? ScopeId { get; }
    public static IntentContext? Current => Ambient.Value;
    public IIntentUsageEntry[] Usage { get { lock (_gate) { return [.. _usage]; } } }
    public IntentUsageBatch Snapshot() => new(IntentId, ScopeId, Usage);
    public void AddUsage(IIntentUsageEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_gate) { ObjectDisposedException.ThrowIf(_disposed, this); _usage.Add(entry); }
    }
    public static IntentContext Begin(string intentId, string? scopeId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(intentId);
        var context = new IntentContext(intentId, scopeId) { _ownsAmbient = true };
        Ambient.Value = context;
        return context;
    }
    public static IntentContext Create(string intentId, string? scopeId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(intentId);
        return new(intentId, scopeId);
    }
    public IDisposable Enter()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var scope = new AmbientScope(this, Ambient.Value);
        Ambient.Value = this;
        return scope;
    }
    private sealed class AmbientScope(IntentContext owner, IntentContext? previous) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) { return; }
            if (!ReferenceEquals(Ambient.Value, owner)) { throw new InvalidOperationException("Intent scopes must be disposed in reverse creation order."); }
            Ambient.Value = previous;
            _disposed = true;
        }
    }
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) { return; }
            if (_ownsAmbient && !ReferenceEquals(Ambient.Value, this))
            { throw new InvalidOperationException("Intent scopes must be disposed in reverse creation order in their owning context."); }
            _disposed = true;
            if (_ownsAmbient) { Ambient.Value = _previous; }
        }
    }
}
