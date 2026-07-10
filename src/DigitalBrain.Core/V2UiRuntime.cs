using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DigitalBrain.Core.V2;

public sealed record V2FeedItem(long Sequence, TenantId TenantId, WorkspaceId WorkspaceId, string SurfaceId, int Revision, string ContentHash, JsonElement Payload);
public sealed record V2FeedCursor(long Sequence, string Nonce);
public sealed record V2FeedPage(IReadOnlyList<V2FeedItem> Items, V2FeedCursor? Next, bool ResetRequired);

public sealed class V2PrivateFeedStore
{
    private readonly ConcurrentDictionary<(string Tenant, string Workspace), List<V2FeedItem>> _feeds = new();
    private readonly ConcurrentDictionary<(string Tenant, string Workspace, string Principal), long> _acks = new();

    public V2FeedItem Append(RequestContext context, string surfaceId, int revision, string contentHash, JsonElement payload)
    {
        var key = (context.TenantId.Value, context.WorkspaceId.Value);
        var list = _feeds.GetOrAdd(key, _ => []);
        lock (list)
        {
            var item = new V2FeedItem(list.Count == 0 ? 1 : list[^1].Sequence + 1, context.TenantId, context.WorkspaceId, surfaceId, revision, contentHash, payload.Clone());
            list.Add(item);
            return item;
        }
    }

    public V2FeedPage CatchUp(RequestContext context, long? after, int limit = 50)
    {
        var key = (context.TenantId.Value, context.WorkspaceId.Value);
        if (!_feeds.TryGetValue(key, out var list)) return new([], null, false);
        lock (list)
        {
            var first = list.Count == 0 ? 1 : list[0].Sequence;
            var requested = after ?? first - 1;
            var reset = requested < first - 1;
            var items = list.Where(x => x.Sequence > requested).Take(Math.Clamp(limit, 1, 200)).ToArray();
            var next = items.Length == 0 || items[^1].Sequence >= list[^1].Sequence ? null : new V2FeedCursor(items[^1].Sequence, CursorNonce(context, items[^1].Sequence));
            return new(items, next, reset);
        }
    }

    public void RetainFrom(RequestContext context, long minimumSequence)
    {
        var key = (context.TenantId.Value, context.WorkspaceId.Value);
        if (!_feeds.TryGetValue(key, out var list)) return;
        lock (list) list.RemoveAll(x => x.Sequence < minimumSequence);
    }

    public void Acknowledge(RequestContext context, long sequence) => _acks[(context.TenantId.Value, context.WorkspaceId.Value, context.Principal.Value)] = sequence;
    public long? Acknowledged(RequestContext context) => _acks.TryGetValue((context.TenantId.Value, context.WorkspaceId.Value, context.Principal.Value), out var sequence) ? sequence : null;
    private static string CursorNonce(RequestContext context, long sequence) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{context.TenantId}:{context.WorkspaceId}:{sequence}")))[..16];
}

public sealed class FileV2PrivateFeedStore(string root)
{
    private readonly object _gate = new();
    private readonly string _path = Path.Combine(root, "v2-feeds", "feed.jsonl");

    public V2FeedItem Append(RequestContext context, string surfaceId, int revision, string contentHash, JsonElement payload)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var existing = ReadAll().Where(x => x.TenantId == context.TenantId && x.WorkspaceId == context.WorkspaceId).ToArray();
            var item = new V2FeedItem(existing.Length == 0 ? 1 : existing[^1].Sequence + 1, context.TenantId, context.WorkspaceId, surfaceId, revision, contentHash, payload.Clone());
            File.AppendAllText(_path, JsonSerializer.Serialize(item) + Environment.NewLine);
            return item;
        }
    }

    public V2FeedPage CatchUp(RequestContext context, long? after, int limit = 50)
    {
        lock (_gate)
        {
            var items = ReadAll().Where(x => x.TenantId == context.TenantId && x.WorkspaceId == context.WorkspaceId).OrderBy(x => x.Sequence).ToArray();
            if (items.Length == 0) return new([], null, false);
            var requested = after ?? items[0].Sequence - 1;
            var reset = requested < items[0].Sequence - 1;
            var page = items.Where(x => x.Sequence > requested).Take(Math.Clamp(limit, 1, 200)).ToArray();
            return new(page, null, reset);
        }
    }

    private IReadOnlyList<V2FeedItem> ReadAll() => !File.Exists(_path) ? Array.Empty<V2FeedItem>() : File.ReadLines(_path).Where(x => !string.IsNullOrWhiteSpace(x)).Select(line => JsonSerializer.Deserialize<V2FeedItem>(line)).Where(x => x is not null).Cast<V2FeedItem>().ToArray();
}

public sealed record V2ActionBinding(string BindingId, string TemplateId, int TemplateVersion, string InputSchemaRef, int MaxUses, DateTimeOffset ExpiresAt, string IssuedTokenHash);
public sealed record V2UiActionUseRecord(string BindingId, string OperationId, string IdempotencyKey, DateTimeOffset UsedAt);
public sealed record V2ActionSubmission(string OperationId, string IdempotencyKey, JsonElement Input);

public sealed class V2ActionExecutor
{
    private readonly ConcurrentDictionary<string, V2ActionBinding> _bindings = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _uses = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, V2UiActionUseRecord> _records = new(StringComparer.Ordinal);

    public void Register(V2ActionBinding binding) => _bindings[binding.BindingId] = binding;

    public V2ActionSubmission Use(RequestContext context, string bindingId, string issuedToken, JsonElement input)
    {
        if (!_bindings.TryGetValue(bindingId, out var binding) || binding.ExpiresAt <= DateTimeOffset.UtcNow) throw new UnauthorizedAccessException("V2 action binding is unavailable.");
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(issuedToken)));
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(binding.IssuedTokenHash), Convert.FromHexString(tokenHash))) throw new UnauthorizedAccessException("V2 action token is invalid.");
        var use = _uses.AddOrUpdate(bindingId, 1, (_, current) => current + 1);
        if (use > binding.MaxUses) { _uses.AddOrUpdate(bindingId, 0, (_, current) => current - 1); throw new InvalidOperationException("V2 action binding usage limit exceeded."); }
        var operationId = $"v2-op-{Guid.NewGuid():N}";
        var idempotency = $"v2-action-{Guid.NewGuid():N}";
        _records[idempotency] = new V2UiActionUseRecord(bindingId, operationId, idempotency, DateTimeOffset.UtcNow);
        return new(operationId, idempotency, input.Clone());
    }

    public bool TryGetUse(string idempotencyKey, out V2UiActionUseRecord? record) => _records.TryGetValue(idempotencyKey, out record);
}
