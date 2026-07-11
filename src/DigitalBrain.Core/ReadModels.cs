using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DigitalBrain.Core.Runtime;

public sealed record TimelineEntry(string Id, TenantId TenantId, WorkspaceId WorkspaceId, DateTimeOffset OccurredAt, string Type, string Summary, string CorrelationId);
public sealed record WorkflowStatusView(string WorkflowId, TenantId TenantId, WorkspaceId WorkspaceId, WorkflowState State, DateTimeOffset UpdatedAt, string? SafeReason);
public enum ConnectorProjectionStatus { Connected, NeedsAuth, InsufficientGrant, Expired, Revoked, ReauthorizationRequired, Unavailable }
public sealed record ConnectorStatusView(string Provider, TenantId TenantId, WorkspaceId WorkspaceId, ConnectorProjectionStatus Status, IReadOnlyList<string> Capabilities, DateTimeOffset CheckedAt);

public interface IProjectionQueryPort
{
    Task<Page<TimelineEntry>> TimelineAsync(RequestContext context, string? cursor, int limit, CancellationToken cancellationToken = default);
    Task<Page<WorkflowStatusView>> WorkflowsAsync(RequestContext context, string? cursor, int limit, CancellationToken cancellationToken = default);
    Task<Page<ConnectorStatusView>> ConnectorsAsync(RequestContext context, string? cursor, int limit, CancellationToken cancellationToken = default);
}

public sealed class InMemoryProjectionQueryStore : IProjectionQueryPort
{
    private readonly CapabilityIsolationGate _gate = new();
    private readonly byte[] _key;
    private readonly ConcurrentBag<TimelineEntry> _timeline = [];
    private readonly ConcurrentBag<WorkflowStatusView> _workflows = [];
    private readonly ConcurrentBag<ConnectorStatusView> _connectors = [];
    public InMemoryProjectionQueryStore(byte[]? cursorKey = null) => _key = cursorKey is { Length: >= 32 } ? cursorKey.ToArray() : SHA256.HashData(Encoding.UTF8.GetBytes("v2-query-cursor-key"));
    public void Add(TimelineEntry entry) => _timeline.Add(entry);
    public void Add(WorkflowStatusView view) => _workflows.Add(view);
    public void Add(ConnectorStatusView view) => _connectors.Add(view);
    public Task<Page<TimelineEntry>> TimelineAsync(RequestContext context, string? cursor, int limit, CancellationToken cancellationToken = default) => Page(context, "brain.read", _timeline.Where(x => x.TenantId == context.TenantId && x.WorkspaceId == context.WorkspaceId).OrderByDescending(x => x.OccurredAt).ToArray(), cursor, limit);
    public Task<Page<WorkflowStatusView>> WorkflowsAsync(RequestContext context, string? cursor, int limit, CancellationToken cancellationToken = default) => Page(context, "brain.read", _workflows.Where(x => x.TenantId == context.TenantId && x.WorkspaceId == context.WorkspaceId).OrderByDescending(x => x.UpdatedAt).ToArray(), cursor, limit);
    public Task<Page<ConnectorStatusView>> ConnectorsAsync(RequestContext context, string? cursor, int limit, CancellationToken cancellationToken = default) => Page(context, "brain.read", _connectors.Where(x => x.TenantId == context.TenantId && x.WorkspaceId == context.WorkspaceId).OrderBy(x => x.Provider, StringComparer.Ordinal).ToArray(), cursor, limit);
    private Task<Page<T>> Page<T>(RequestContext context, string grant, IReadOnlyList<T> values, string? cursor, int limit)
    {
        _gate.Demand(context, context.TenantId, context.WorkspaceId, grant);
        var offset = Decode(cursor);
        var take = Math.Clamp(limit, 1, 100);
        var items = values.Skip(offset).Take(take).ToArray();
        return Task.FromResult(new Page<T>(items, offset + items.Length < values.Count ? Encode(offset + items.Length) : null, offset + items.Length < values.Count));
    }
    private string Encode(int offset) { var b = offset.ToString(); var s = Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(b))); return Convert.ToBase64String(Encoding.UTF8.GetBytes(b + "." + s)).TrimEnd('=').Replace('+', '-').Replace('/', '_'); }
    private int Decode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return 0;
        try { var b = cursor.Replace('-', '+').Replace('_', '/'); b += (b.Length % 4) switch { 2 => "==", 3 => "=", _ => "" }; var p = Encoding.UTF8.GetString(Convert.FromBase64String(b)).Split('.'); var expected = Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(p[0]))); if (p.Length != 2 || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(p[1])) || !int.TryParse(p[0], out var n) || n < 0) throw new FormatException(); return n; } catch (Exception ex) when (ex is FormatException or IndexOutOfRangeException) { throw new ArgumentException("Invalid projection cursor.", nameof(cursor)); }
    }
}

/// <summary>Append-only local projection store used by Development/Test hosts.</summary>
public sealed class FileProjectionQueryStore : IProjectionQueryPort
{
    private readonly InMemoryProjectionQueryStore _inner;
    private readonly string _path;
    private readonly object _lock = new();

    public FileProjectionQueryStore(string path, byte[]? cursorKey = null)
    {
        _path = path;
        _inner = new InMemoryProjectionQueryStore(cursorKey);
        if (File.Exists(path))
        {
            foreach (var line in File.ReadLines(path).Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                try
                {
                    var record = JsonSerializer.Deserialize<Record>(line);
                    if (record?.Kind == "timeline" && record.Timeline is not null) _inner.Add(record.Timeline);
                    else if (record?.Kind == "workflow" && record.Workflow is not null) _inner.Add(record.Workflow);
                    else if (record?.Kind == "connector" && record.Connector is not null) _inner.Add(record.Connector);
                }
                catch (JsonException) { /* quarantine torn/poison projection record */ }
            }
        }
    }

    public void Add(TimelineEntry value) => Append(new("timeline", value, null, null), () => _inner.Add(value));
    public void Add(WorkflowStatusView value) => Append(new("workflow", null, value, null), () => _inner.Add(value));
    public void Add(ConnectorStatusView value) => Append(new("connector", null, null, value), () => _inner.Add(value));
    public Task<Page<TimelineEntry>> TimelineAsync(RequestContext c, string? cursor, int limit, CancellationToken ct = default) => _inner.TimelineAsync(c, cursor, limit, ct);
    public Task<Page<WorkflowStatusView>> WorkflowsAsync(RequestContext c, string? cursor, int limit, CancellationToken ct = default) => _inner.WorkflowsAsync(c, cursor, limit, ct);
    public Task<Page<ConnectorStatusView>> ConnectorsAsync(RequestContext c, string? cursor, int limit, CancellationToken ct = default) => _inner.ConnectorsAsync(c, cursor, limit, ct);

    private void Append(Record record, Action apply)
    {
        lock (_lock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            File.AppendAllText(_path, JsonSerializer.Serialize(record) + Environment.NewLine);
            apply();
        }
    }

    private sealed record Record(string Kind, TimelineEntry? Timeline, WorkflowStatusView? Workflow, ConnectorStatusView? Connector);
}
