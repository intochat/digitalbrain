using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DigitalBrain.Core.V2;

public sealed record V2TimelineEntry(string Id, TenantId TenantId, WorkspaceId WorkspaceId, DateTimeOffset OccurredAt, string Type, string Summary, string CorrelationId);
public sealed record V2WorkflowStatusView(string WorkflowId, TenantId TenantId, WorkspaceId WorkspaceId, WorkflowState State, DateTimeOffset UpdatedAt, string? SafeReason);
public enum V2ConnectorProjectionStatus { Connected, NeedsAuth, InsufficientGrant, Expired, Revoked, ReauthorizationRequired, Unavailable }
public sealed record V2ConnectorStatusView(string Provider, TenantId TenantId, WorkspaceId WorkspaceId, V2ConnectorProjectionStatus Status, IReadOnlyList<string> Capabilities, DateTimeOffset CheckedAt);

public interface IV2ProjectionQueryPort
{
    Task<V2Page<V2TimelineEntry>> TimelineAsync(RequestContext context, string? cursor, int limit, CancellationToken cancellationToken = default);
    Task<V2Page<V2WorkflowStatusView>> WorkflowsAsync(RequestContext context, string? cursor, int limit, CancellationToken cancellationToken = default);
    Task<V2Page<V2ConnectorStatusView>> ConnectorsAsync(RequestContext context, string? cursor, int limit, CancellationToken cancellationToken = default);
}

public sealed class InMemoryV2ProjectionQueryStore : IV2ProjectionQueryPort
{
    private readonly CapabilityIsolationGate _gate = new();
    private readonly byte[] _key;
    private readonly ConcurrentBag<V2TimelineEntry> _timeline = [];
    private readonly ConcurrentBag<V2WorkflowStatusView> _workflows = [];
    private readonly ConcurrentBag<V2ConnectorStatusView> _connectors = [];
    public InMemoryV2ProjectionQueryStore(byte[]? cursorKey = null) => _key = cursorKey is { Length: >= 32 } ? cursorKey.ToArray() : SHA256.HashData(Encoding.UTF8.GetBytes("v2-query-cursor-key"));
    public void Add(V2TimelineEntry entry) => _timeline.Add(entry);
    public void Add(V2WorkflowStatusView view) => _workflows.Add(view);
    public void Add(V2ConnectorStatusView view) => _connectors.Add(view);
    public Task<V2Page<V2TimelineEntry>> TimelineAsync(RequestContext context, string? cursor, int limit, CancellationToken cancellationToken = default) => Page(context, "brain.read", _timeline.Where(x => x.TenantId == context.TenantId && x.WorkspaceId == context.WorkspaceId).OrderByDescending(x => x.OccurredAt).ToArray(), cursor, limit);
    public Task<V2Page<V2WorkflowStatusView>> WorkflowsAsync(RequestContext context, string? cursor, int limit, CancellationToken cancellationToken = default) => Page(context, "brain.read", _workflows.Where(x => x.TenantId == context.TenantId && x.WorkspaceId == context.WorkspaceId).OrderByDescending(x => x.UpdatedAt).ToArray(), cursor, limit);
    public Task<V2Page<V2ConnectorStatusView>> ConnectorsAsync(RequestContext context, string? cursor, int limit, CancellationToken cancellationToken = default) => Page(context, "brain.read", _connectors.Where(x => x.TenantId == context.TenantId && x.WorkspaceId == context.WorkspaceId).OrderBy(x => x.Provider, StringComparer.Ordinal).ToArray(), cursor, limit);
    private Task<V2Page<T>> Page<T>(RequestContext context, string grant, IReadOnlyList<T> values, string? cursor, int limit)
    {
        _gate.Demand(context, context.TenantId, context.WorkspaceId, grant);
        var offset = Decode(cursor);
        var take = Math.Clamp(limit, 1, 100);
        var items = values.Skip(offset).Take(take).ToArray();
        return Task.FromResult(new V2Page<T>(items, offset + items.Length < values.Count ? Encode(offset + items.Length) : null, offset + items.Length < values.Count));
    }
    private string Encode(int offset) { var b = offset.ToString(); var s = Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(b))); return Convert.ToBase64String(Encoding.UTF8.GetBytes(b + "." + s)).TrimEnd('=').Replace('+', '-').Replace('/', '_'); }
    private int Decode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return 0;
        try { var b = cursor.Replace('-', '+').Replace('_', '/'); b += (b.Length % 4) switch { 2 => "==", 3 => "=", _ => "" }; var p = Encoding.UTF8.GetString(Convert.FromBase64String(b)).Split('.'); var expected = Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(p[0]))); if (p.Length != 2 || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(p[1])) || !int.TryParse(p[0], out var n) || n < 0) throw new FormatException(); return n; } catch (Exception ex) when (ex is FormatException or IndexOutOfRangeException) { throw new ArgumentException("Invalid V2 projection cursor.", nameof(cursor)); }
    }
}

/// <summary>Append-only local V2 projection store used by Development/Test hosts.</summary>
public sealed class FileV2ProjectionQueryStore : IV2ProjectionQueryPort
{
    private readonly InMemoryV2ProjectionQueryStore _inner;
    private readonly string _path;
    private readonly object _lock = new();

    public FileV2ProjectionQueryStore(string path, byte[]? cursorKey = null)
    {
        _path = path;
        _inner = new InMemoryV2ProjectionQueryStore(cursorKey);
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

    public void Add(V2TimelineEntry value) => Append(new("timeline", value, null, null), () => _inner.Add(value));
    public void Add(V2WorkflowStatusView value) => Append(new("workflow", null, value, null), () => _inner.Add(value));
    public void Add(V2ConnectorStatusView value) => Append(new("connector", null, null, value), () => _inner.Add(value));
    public Task<V2Page<V2TimelineEntry>> TimelineAsync(RequestContext c, string? cursor, int limit, CancellationToken ct = default) => _inner.TimelineAsync(c, cursor, limit, ct);
    public Task<V2Page<V2WorkflowStatusView>> WorkflowsAsync(RequestContext c, string? cursor, int limit, CancellationToken ct = default) => _inner.WorkflowsAsync(c, cursor, limit, ct);
    public Task<V2Page<V2ConnectorStatusView>> ConnectorsAsync(RequestContext c, string? cursor, int limit, CancellationToken ct = default) => _inner.ConnectorsAsync(c, cursor, limit, ct);

    private void Append(Record record, Action apply)
    {
        lock (_lock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            File.AppendAllText(_path, JsonSerializer.Serialize(record) + Environment.NewLine);
            apply();
        }
    }

    private sealed record Record(string Kind, V2TimelineEntry? Timeline, V2WorkflowStatusView? Workflow, V2ConnectorStatusView? Connector);
}
