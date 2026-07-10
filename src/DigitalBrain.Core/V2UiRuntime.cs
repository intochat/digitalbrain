using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DigitalBrain.Core.V2;

public enum V2SurfaceAudienceKind
{
    Principal,
    Workspace,
    Public
}

public sealed record V2SurfaceAudience(V2SurfaceAudienceKind Kind, string Id);

public static class V2PrincipalScope
{
    public static string Id(PrincipalRef principal)
    {
        var canonical = JsonSerializer.SerializeToUtf8Bytes(new
        {
            kind = (int)principal.Kind,
            value = principal.Value
        });
        return $"p{(int)principal.Kind}-{Convert.ToHexString(SHA256.HashData(canonical)).ToLowerInvariant()}";
    }
}

/// <summary>A token-free action description safe to persist with a surface record.</summary>
public sealed record V2StoredActionBinding(
    string BindingId,
    string ActionType,
    string InputSchemaRef,
    string RequiredGrant,
    int MaxUses,
    DateTimeOffset ExpiresAt,
    int ActionSchemaVersion = V2UiProtocol.ActionSchemaVersion);

/// <summary>
/// Durable V2 UI record. Wire action tokens are deliberately absent and are minted for the authenticated
/// recipient each time this record is delivered.
/// </summary>
public sealed record V2StoredSurfaceRecord(
    long Sequence,
    TenantId TenantId,
    WorkspaceId WorkspaceId,
    V2SurfaceAudience Audience,
    string SurfaceId,
    int Revision,
    string ContentHash,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    string CorrelationId,
    string CauseKind,
    string CauseId,
    IReadOnlyList<string> RequiredClientCapabilities,
    JsonElement Payload,
    IReadOnlyList<V2StoredActionBinding> Actions,
    int ProtocolVersion = V2UiProtocol.ProtocolVersion,
    string SurfaceSchema = V2UiProtocol.SurfaceSchema,
    int SurfaceSchemaVersion = V2UiProtocol.SurfaceSchemaVersion,
    int ActionSchemaVersion = V2UiProtocol.ActionSchemaVersion,
    PrincipalKind? AudiencePrincipalKind = null);

public sealed record V2FeedCursor(long Sequence, string Nonce);
public sealed record V2FeedPage(
    IReadOnlyList<V2StoredSurfaceRecord> Items,
    V2FeedCursor? Next,
    bool ResetRequired,
    bool IsSnapshot = false,
    long LatestSequence = 0);

public static class V2SurfacePayloadPolicy
{
    private static readonly HashSet<string> ForbiddenKeys = new(StringComparer.Ordinal)
    {
        "accesstoken", "actiontoken", "authorization", "authorizationcode", "clientid", "clientsecret",
        "codeverifier", "grants", "password", "principal", "principalid", "refreshtoken", "secret", "secretvalue",
        "sessionid", "tenantid", "userid", "workspaceid"
    };

    public static void DemandSafe(JsonElement value, int depth = 0)
    {
        if (depth > 64) throw new ArgumentException("The V2 surface payload exceeds the nesting bound.", nameof(value));
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject())
            {
                var normalized = new string(property.Name.Where(static character =>
                    character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9')
                    .Select(static character => char.ToLowerInvariant(character)).ToArray());
                if (ForbiddenKeys.Contains(normalized))
                    throw new ArgumentException("The V2 surface payload contains a forbidden sensitive field.", nameof(value));
                DemandSafe(property.Value, depth + 1);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray()) DemandSafe(item, depth + 1);
        }
        else if (value.ValueKind is not (JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or
                 JsonValueKind.False or JsonValueKind.Null))
        {
            throw new ArgumentException("The V2 surface payload contains an unsupported JSON value.", nameof(value));
        }
    }
}

public interface IV2PrivateFeedStore
{
    V2StoredSurfaceRecord Append(
        RequestContext context,
        V2SurfaceAudienceKind audienceKind,
        string surfaceId,
        int revision,
        string contentHash,
        DateTimeOffset createdAt,
        DateTimeOffset? expiresAt,
        string correlationId,
        string causeKind,
        string causeId,
        IReadOnlyList<string> requiredClientCapabilities,
        JsonElement payload,
        IReadOnlyList<V2StoredActionBinding> actions,
        int protocolVersion = V2UiProtocol.ProtocolVersion,
        string surfaceSchema = V2UiProtocol.SurfaceSchema,
        int surfaceSchemaVersion = V2UiProtocol.SurfaceSchemaVersion,
        int actionSchemaVersion = V2UiProtocol.ActionSchemaVersion);

    V2StoredSurfaceRecord EnsureInitial(
        RequestContext context,
        V2SurfaceAudienceKind audienceKind,
        string surfaceId,
        Func<long, V2StoredSurfaceRecord> factory);

    V2FeedPage CatchUp(RequestContext context, V2SurfaceAudienceKind audienceKind, long? after, int limit = 50);
    ValueTask WaitForChangeAsync(RequestContext context, V2SurfaceAudienceKind audienceKind, long after, CancellationToken cancellationToken);
    void RetainFrom(RequestContext context, V2SurfaceAudienceKind audienceKind, long minimumSequence);
    void MarkDelivered(RequestContext context, V2SurfaceAudienceKind audienceKind, long sequence);
    ValueTask<bool> WaitUntilDeliveredAsync(RequestContext context, V2SurfaceAudienceKind audienceKind, long sequence, CancellationToken cancellationToken);
    void Acknowledge(RequestContext context, V2SurfaceAudienceKind audienceKind, long sequence);
    long? Acknowledged(RequestContext context, V2SurfaceAudienceKind audienceKind);
    int? LatestRevision(RequestContext context, V2SurfaceAudienceKind audienceKind, string surfaceId);
}

/// <summary>
/// Bounded-page, workspace-private feed store. Supplying <paramref name="storagePath"/> enables an append-only
/// durable log. Subscribers wait on a rotating task signal and then pull bounded pages, so a slow client never
/// creates an unbounded channel and records are never silently dropped.
/// </summary>
public sealed class V2PrivateFeedStore : IV2PrivateFeedStore
{
    // 32 active records * (32 KiB payload + bounded metadata/actions) stays comfortably below the
    // transport's 2 MiB send ceiling when an atomic reset snapshot is serialized into one message.
    public const int MaximumActiveSurfacesPerAudience = 32;
    public const int MaximumSurfacePayloadBytes = 32 * 1024;
    public const int MaximumActionsPerSurface = 16;
    public const int MaximumCapabilitiesPerSurface = 32;
    private readonly ConcurrentDictionary<FeedKey, FeedState> _feeds = new();
    private readonly ConcurrentDictionary<AckKey, long> _acks = new();
    private readonly ConcurrentDictionary<AckKey, long> _delivered = new();
    private readonly ConcurrentDictionary<AckKey, TaskCompletionSource<long>> _deliverySignals = new();
    private readonly string? _storagePath;
    private readonly Action<string>? _appendLine;
    private readonly byte[]? _integrityKey;
    private readonly object _persistenceGate = new();

    public V2PrivateFeedStore(string? storagePath = null, Action<string>? appendLine = null, byte[]? integrityKey = null)
    {
        _storagePath = string.IsNullOrWhiteSpace(storagePath) ? null : Path.GetFullPath(storagePath);
        if (integrityKey is { Length: < 32 }) throw new ArgumentException("The V2 feed integrity key must be at least 256 bits.", nameof(integrityKey));
        _integrityKey = integrityKey?.ToArray();
        _appendLine = appendLine ?? (_storagePath is null
            ? null
            : line =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_storagePath)!);
                File.AppendAllText(_storagePath, line + Environment.NewLine);
            });
        Load();
    }

    public V2StoredSurfaceRecord Append(
        RequestContext context,
        V2SurfaceAudienceKind audienceKind,
        string surfaceId,
        int revision,
        string contentHash,
        DateTimeOffset createdAt,
        DateTimeOffset? expiresAt,
        string correlationId,
        string causeKind,
        string causeId,
        IReadOnlyList<string> requiredClientCapabilities,
        JsonElement payload,
        IReadOnlyList<V2StoredActionBinding> actions,
        int protocolVersion = V2UiProtocol.ProtocolVersion,
        string surfaceSchema = V2UiProtocol.SurfaceSchema,
        int surfaceSchemaVersion = V2UiProtocol.SurfaceSchemaVersion,
        int actionSchemaVersion = V2UiProtocol.ActionSchemaVersion)
    {
        ValidateContext(context);
        ValidateRecordInput(surfaceId, revision, contentHash, correlationId, causeKind, causeId, requiredClientCapabilities, payload, actions,
            protocolVersion, surfaceSchema, surfaceSchemaVersion, actionSchemaVersion);
        var key = Key(context, audienceKind);
        var state = _feeds.GetOrAdd(key, static _ => new FeedState());
        V2StoredSurfaceRecord record;
        TaskCompletionSource<long> changed;
        lock (state.Gate)
        {
            state.Current.TryGetValue(surfaceId, out var current);
            if (current is not null && revision <= current.Revision)
                throw new InvalidOperationException("V2 surface revisions must increase monotonically.");
            if (current is null && state.Current.Count >= MaximumActiveSurfacesPerAudience)
                throw new InvalidOperationException("The V2 feed active-surface bound has been reached.");
            var sequence = checked(state.LastSequence + 1);
            record = new V2StoredSurfaceRecord(
                sequence,
                context.TenantId,
                context.WorkspaceId,
                Audience(context, audienceKind),
                surfaceId,
                revision,
                NormalizeHash(contentHash),
                createdAt.ToUniversalTime(),
                expiresAt?.ToUniversalTime(),
                correlationId,
                causeKind,
                causeId,
                requiredClientCapabilities.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                payload.Clone(),
                actions.ToArray(),
                protocolVersion,
                surfaceSchema,
                surfaceSchemaVersion,
                actionSchemaVersion,
                audienceKind == V2SurfaceAudienceKind.Principal ? context.Principal.Kind : null);
            Persist(new FeedLogEntry("append", record, null, null, null, null, null, null, 0));
            state.Items.Add(record);
            state.Current[surfaceId] = record;
            state.LastSequence = sequence;
            changed = state.Changed;
            state.Changed = NewSignal();
        }
        changed.TrySetResult(record.Sequence);
        return record;
    }

    public V2StoredSurfaceRecord EnsureInitial(
        RequestContext context,
        V2SurfaceAudienceKind audienceKind,
        string surfaceId,
        Func<long, V2StoredSurfaceRecord> factory)
    {
        ValidateContext(context);
        var key = Key(context, audienceKind);
        var state = _feeds.GetOrAdd(key, static _ => new FeedState());
        V2StoredSurfaceRecord? created = null;
        TaskCompletionSource<long>? changed = null;
        lock (state.Gate)
        {
            state.Current.TryGetValue(surfaceId, out var existing);
            if (existing is not null) return existing;
            if (state.Current.Count >= MaximumActiveSurfacesPerAudience)
                throw new InvalidOperationException("The V2 feed active-surface bound has been reached.");
            var sequence = checked(state.LastSequence + 1);
            var candidate = factory(sequence);
            ValidateOwnedRecord(context, audienceKind, surfaceId, sequence, candidate);
            ValidateRecordInput(candidate.SurfaceId, candidate.Revision, candidate.ContentHash, candidate.CorrelationId,
                candidate.CauseKind, candidate.CauseId, candidate.RequiredClientCapabilities, candidate.Payload, candidate.Actions,
                candidate.ProtocolVersion, candidate.SurfaceSchema, candidate.SurfaceSchemaVersion, candidate.ActionSchemaVersion);
            created = candidate with
            {
                ContentHash = NormalizeHash(candidate.ContentHash),
                CreatedAt = candidate.CreatedAt.ToUniversalTime(),
                ExpiresAt = candidate.ExpiresAt?.ToUniversalTime(),
                RequiredClientCapabilities = candidate.RequiredClientCapabilities.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                Payload = candidate.Payload.Clone(),
                Actions = candidate.Actions.ToArray()
            };
            Persist(new FeedLogEntry("append", created, null, null, null, null, null, null, 0));
            state.Items.Add(created);
            state.Current[surfaceId] = created;
            state.LastSequence = sequence;
            changed = state.Changed;
            state.Changed = NewSignal();
        }
        changed!.TrySetResult(created!.Sequence);
        return created;
    }

    // Compatibility overload retained for existing V2 unit consumers; new code must select an audience explicitly.
    public V2StoredSurfaceRecord Append(RequestContext context, string surfaceId, int revision, string contentHash, JsonElement payload)
    {
        var compatibleHash = IsHash(contentHash)
            ? contentHash
            : V2SurfaceContentHash.Compute(payload, []);
        return Append(context, V2SurfaceAudienceKind.Workspace, surfaceId, revision, compatibleHash, DateTimeOffset.UtcNow, null,
            context.CorrelationId, "surface", surfaceId, [], payload, []);
    }

    public V2FeedPage CatchUp(RequestContext context, V2SurfaceAudienceKind audienceKind, long? after, int limit = 50)
    {
        var requested = after ?? 0;
        if (requested < 0) throw new ArgumentOutOfRangeException(nameof(after));
        var key = Key(context, audienceKind);
        if (!_feeds.TryGetValue(key, out var state)) return new([], null, false, false, 0);
        lock (state.Gate)
        {
            if (state.Items.Count == 0)
            {
                if (state.LastSequence == 0) return new([], null, false, false, 0);
                return requested == state.LastSequence
                    ? new([], null, false, false, state.LastSequence)
                    : new([], null, true, true, state.LastSequence);
            }
            var first = state.Items[0].Sequence;
            var latest = state.LastSequence;
            if (requested < first - 1 || requested > latest)
                return SnapshotPage(state, latest);

            var take = Math.Clamp(limit, 1, 200);
            var start = FirstSequenceAfter(state.Items, requested);
            if (start < 0)
                return requested == latest
                    ? new([], null, false, false, latest)
                    : SnapshotPage(state, latest);

            var expected = checked(requested + 1);
            var items = new List<V2StoredSurfaceRecord>(Math.Min(take, state.Items.Count - start));
            var index = start;
            var now = DateTimeOffset.UtcNow;
            while (index < state.Items.Count && items.Count < take)
            {
                var item = state.Items[index++];
                if (item.Sequence != expected || item.ExpiresAt is { } expiry && expiry <= now)
                    return SnapshotPage(state, latest);
                items.Add(item);
                expected = checked(expected + 1);
            }
            // Inspect one item past the bounded page so a discontinuity at the page edge is reset
            // atomically without materializing the retained tail.
            if (index < state.Items.Count && state.Items[index].Sequence != expected) return SnapshotPage(state, latest);
            if (index >= state.Items.Count && expected <= latest) return SnapshotPage(state, latest);

            var next = items.Count > 0 && items[^1].Sequence < latest
                ? new V2FeedCursor(items[^1].Sequence, CursorNonce(context, audienceKind, items[^1].Sequence))
                : null;
            return new(items.ToArray(), next, false, false, latest);
        }
    }

    private static int FirstSequenceAfter(List<V2StoredSurfaceRecord> items, long sequence)
    {
        var low = 0;
        var high = items.Count;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (items[middle].Sequence <= sequence) low = middle + 1;
            else high = middle;
        }
        return low < items.Count ? low : -1;
    }

    private static V2FeedPage SnapshotPage(FeedState state, long latest)
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = state.Current.Values
            .Where(item => item.ExpiresAt is not { } expiry || expiry > now)
            .OrderBy(static item => item.Sequence)
            .Take(MaximumActiveSurfacesPerAudience)
            .ToArray();
        return new(snapshot, null, true, true, latest);
    }

    public V2FeedPage CatchUp(RequestContext context, long? after, int limit = 50) =>
        CatchUp(context, V2SurfaceAudienceKind.Workspace, after, limit);

    public async ValueTask WaitForChangeAsync(
        RequestContext context,
        V2SurfaceAudienceKind audienceKind,
        long after,
        CancellationToken cancellationToken)
    {
        var state = _feeds.GetOrAdd(Key(context, audienceKind), static _ => new FeedState());
        Task wait;
        lock (state.Gate)
        {
            if (state.LastSequence > after) return;
            wait = state.Changed.Task;
        }
        await wait.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public void RetainFrom(RequestContext context, V2SurfaceAudienceKind audienceKind, long minimumSequence)
    {
        if (minimumSequence < 1) throw new ArgumentOutOfRangeException(nameof(minimumSequence));
        var key = Key(context, audienceKind);
        if (!_feeds.TryGetValue(key, out var state)) return;
        lock (state.Gate)
        {
            var currentSequences = state.Current.ToDictionary(static pair => pair.Key, static pair => pair.Value.Sequence, StringComparer.Ordinal);
            var retained = state.Items
                .Where(item => item.Sequence >= minimumSequence || currentSequences[item.SurfaceId] == item.Sequence)
                .ToArray();
            if (retained.Length == state.Items.Count) return;
            Persist(new FeedLogEntry("retain", null, key.Tenant, key.Workspace, key.AudienceKind, key.AudienceId,
                key.AudiencePrincipalKind, null, minimumSequence));
            state.Items.Clear();
            state.Items.AddRange(retained);
        }
    }

    public void RetainFrom(RequestContext context, long minimumSequence) =>
        RetainFrom(context, V2SurfaceAudienceKind.Workspace, minimumSequence);

    public void Acknowledge(RequestContext context, V2SurfaceAudienceKind audienceKind, long sequence)
    {
        if (sequence < 0) throw new ArgumentOutOfRangeException(nameof(sequence));
        var key = Ack(context, audienceKind);
        if (!_delivered.TryGetValue(key, out var delivered) || sequence > delivered)
            throw new InvalidOperationException("Cannot acknowledge an unseen V2 feed sequence.");
        _acks.AddOrUpdate(key, sequence, (_, current) => Math.Max(current, sequence));
    }

    public void MarkDelivered(RequestContext context, V2SurfaceAudienceKind audienceKind, long sequence)
    {
        if (sequence < 0 || sequence > LatestSequence(context, audienceKind))
            throw new InvalidOperationException("Cannot record delivery outside the V2 feed watermark.");
        var key = Ack(context, audienceKind);
        _delivered.AddOrUpdate(key, sequence, (_, current) => Math.Max(current, sequence));
        if (_deliverySignals.TryRemove(key, out var signal)) signal.TrySetResult(sequence);
    }

    public async ValueTask<bool> WaitUntilDeliveredAsync(
        RequestContext context,
        V2SurfaceAudienceKind audienceKind,
        long sequence,
        CancellationToken cancellationToken)
    {
        if (sequence < 0) return false;
        var key = Ack(context, audienceKind);
        while (!cancellationToken.IsCancellationRequested)
        {
            if (_delivered.TryGetValue(key, out var delivered) && delivered >= sequence) return true;
            var signal = _deliverySignals.GetOrAdd(key, static _ => NewSignal());
            if (_delivered.TryGetValue(key, out delivered) && delivered >= sequence) return true;
            try { await signal.Task.WaitAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return false; }
        }
        return false;
    }

    public void Acknowledge(RequestContext context, long sequence) =>
        Acknowledge(context, V2SurfaceAudienceKind.Workspace, sequence);

    public long? Acknowledged(RequestContext context, V2SurfaceAudienceKind audienceKind) =>
        _acks.TryGetValue(Ack(context, audienceKind), out var sequence) ? sequence : null;

    public long? Acknowledged(RequestContext context) => Acknowledged(context, V2SurfaceAudienceKind.Workspace);

    public int? LatestRevision(RequestContext context, V2SurfaceAudienceKind audienceKind, string surfaceId)
    {
        if (!_feeds.TryGetValue(Key(context, audienceKind), out var state)) return null;
        lock (state.Gate)
            return state.Current.TryGetValue(surfaceId, out var current) ? current.Revision : null;
    }

    private long LatestSequence(RequestContext context, V2SurfaceAudienceKind audienceKind)
    {
        if (!_feeds.TryGetValue(Key(context, audienceKind), out var state)) return 0;
        lock (state.Gate) return state.LastSequence;
    }

    private void Load()
    {
        if (_storagePath is null || !File.Exists(_storagePath)) return;
        var lineNumber = 0;
        foreach (var line in File.ReadLines(_storagePath))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line)) continue;
            FeedLogEntry? entry;
            try { entry = JsonSerializer.Deserialize<FeedLogEntry>(line); }
            catch (JsonException)
            {
                Quarantine(lineNumber, line, "invalid-json");
                throw new InvalidDataException($"The V2 UI feed journal contains invalid JSON at line {lineNumber}.");
            }
            if (entry is null || !VerifyIntegrity(entry))
            {
                Quarantine(lineNumber, line, "invalid-integrity");
                throw new InvalidDataException($"The V2 UI feed journal failed integrity validation at line {lineNumber}.");
            }
            if (entry?.Kind == "append" && entry.Record is not null && TryValidateLoadedRecord(entry.Record))
            {
                var key = new FeedKey(entry.Record.TenantId.Value, entry.Record.WorkspaceId.Value,
                    entry.Record.Audience.Kind, entry.Record.Audience.Id, entry.Record.AudiencePrincipalKind);
                var state = _feeds.GetOrAdd(key, static _ => new FeedState());
                lock (state.Gate)
                {
                    state.Current.TryGetValue(entry.Record.SurfaceId, out var current);
                    if (entry.Record.Sequence != checked(state.LastSequence + 1) ||
                        current is not null && entry.Record.Revision <= current.Revision ||
                        current is null && state.Current.Count >= MaximumActiveSurfacesPerAudience)
                    {
                        Quarantine(lineNumber, line, "invalid-sequence-or-revision");
                        throw new InvalidDataException($"The V2 UI feed journal contains an invalid sequence or revision at line {lineNumber}.");
                    }
                    var loaded = entry.Record with
                    {
                        Payload = entry.Record.Payload.Clone(),
                        RequiredClientCapabilities = entry.Record.RequiredClientCapabilities.ToArray(),
                        Actions = entry.Record.Actions.ToArray()
                    };
                    state.Items.Add(loaded);
                    state.Current[loaded.SurfaceId] = loaded;
                    state.LastSequence = loaded.Sequence;
                }
            }
            else if (entry?.Kind == "retain" && IsBoundedId(entry.Tenant) && IsBoundedId(entry.Workspace) &&
                     entry.AudienceKind is not null && Enum.IsDefined(entry.AudienceKind.Value) &&
                     entry.AudienceId is not null && entry.AudienceId.Length <= 256 && entry.MinimumSequence >= 1 &&
                     ValidAudience(entry.AudienceKind.Value, entry.AudienceId, entry.Workspace!, entry.AudiencePrincipalKind))
            {
                var key = new FeedKey(entry.Tenant!, entry.Workspace!, entry.AudienceKind.Value, entry.AudienceId, entry.AudiencePrincipalKind);
                if (_feeds.TryGetValue(key, out var state))
                    lock (state.Gate)
                    {
                        var currentSequences = state.Current.ToDictionary(static pair => pair.Key, static pair => pair.Value.Sequence, StringComparer.Ordinal);
                        state.Items.RemoveAll(item => item.Sequence < entry.MinimumSequence && currentSequences[item.SurfaceId] != item.Sequence);
                    }
            }
            else
            {
                Quarantine(lineNumber, line, "invalid-record");
                throw new InvalidDataException($"The V2 UI feed journal contains an invalid record at line {lineNumber}.");
            }
        }
    }

    private void Persist(FeedLogEntry entry)
    {
        if (_appendLine is null) return;
        lock (_persistenceGate)
            _appendLine(JsonSerializer.Serialize(WithIntegrity(entry)));
    }

    private FeedLogEntry WithIntegrity(FeedLogEntry entry)
    {
        if (_integrityKey is null) return entry with { Integrity = null };
        var unsigned = entry with { Integrity = null };
        var signature = HMACSHA256.HashData(_integrityKey, JsonSerializer.SerializeToUtf8Bytes(unsigned));
        return unsigned with { Integrity = Convert.ToHexString(signature).ToLowerInvariant() };
    }

    private bool VerifyIntegrity(FeedLogEntry entry)
    {
        if (_integrityKey is null) return true;
        if (entry.Integrity is not { Length: 64 } || entry.Integrity.Any(static character => !Uri.IsHexDigit(character))) return false;
        byte[] actual;
        try { actual = Convert.FromHexString(entry.Integrity); }
        catch (FormatException) { return false; }
        var expected = HMACSHA256.HashData(_integrityKey,
            JsonSerializer.SerializeToUtf8Bytes(entry with { Integrity = null }));
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static bool TryValidateLoadedRecord(V2StoredSurfaceRecord record)
    {
        try
        {
            if (record.Sequence < 1 || !IsBoundedId(record.TenantId.Value) || !IsBoundedId(record.WorkspaceId.Value) ||
                record.Audience is null || !Enum.IsDefined(record.Audience.Kind) || record.Audience.Id is null ||
                record.Audience.Id.Length > 256 || !ValidAudience(record.Audience.Kind, record.Audience.Id,
                    record.WorkspaceId.Value, record.AudiencePrincipalKind) ||
                record.RequiredClientCapabilities is null || record.Actions is null ||
                record.CreatedAt == default || record.ExpiresAt is { } expiry && expiry <= record.CreatedAt)
                return false;
            ValidateRecordInput(record.SurfaceId, record.Revision, record.ContentHash, record.CorrelationId,
                record.CauseKind, record.CauseId, record.RequiredClientCapabilities, record.Payload, record.Actions,
                record.ProtocolVersion, record.SurfaceSchema, record.SurfaceSchemaVersion, record.ActionSchemaVersion);
            var normalized = NormalizeHash(record.ContentHash);
            var recomputed = V2SurfaceContentHash.Compute(record.Payload, record.Actions);
            return CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(normalized),
                Encoding.ASCII.GetBytes(recomputed));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
        {
            return false;
        }
    }

    private static bool IsBoundedId(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 256;

    private void Quarantine(int lineNumber, string raw, string reason)
    {
        if (_storagePath is null) return;
        try
        {
            var path = _storagePath + ".quarantine";
            var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
            lock (_persistenceGate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, JsonSerializer.Serialize(new { line = lineNumber, sha256 = digest, reason }) + Environment.NewLine);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static bool ValidAudience(
        V2SurfaceAudienceKind kind,
        string id,
        string workspace,
        PrincipalKind? principalKind) => kind switch
        {
            V2SurfaceAudienceKind.Principal => IsBoundedId(id) && principalKind is not null && Enum.IsDefined(principalKind.Value),
            V2SurfaceAudienceKind.Workspace => principalKind is null && string.Equals(id, workspace, StringComparison.Ordinal),
            V2SurfaceAudienceKind.Public => principalKind is null && id.Length == 0,
            _ => false
        };

    private static void ValidateOwnedRecord(RequestContext context, V2SurfaceAudienceKind audienceKind, string surfaceId, long sequence, V2StoredSurfaceRecord record)
    {
        if (record.Sequence != sequence || record.TenantId != context.TenantId || record.WorkspaceId != context.WorkspaceId ||
            record.Audience != Audience(context, audienceKind) ||
            record.AudiencePrincipalKind != (audienceKind == V2SurfaceAudienceKind.Principal ? context.Principal.Kind : null) ||
            !string.Equals(record.SurfaceId, surfaceId, StringComparison.Ordinal))
            throw new InvalidOperationException("The initial V2 surface factory attempted to escape its authenticated scope.");
    }

    private static void ValidateContext(RequestContext context)
    {
        if (string.IsNullOrWhiteSpace(context.TenantId.Value) || context.TenantId.Value.Length > 256 ||
            string.IsNullOrWhiteSpace(context.WorkspaceId.Value) || context.WorkspaceId.Value.Length > 256 ||
            string.IsNullOrWhiteSpace(context.Principal.Value) || context.Principal.Value.Length > 256)
            throw new ArgumentException("V2 feed scope identifiers must be present and bounded.", nameof(context));
    }

    private static void ValidateRecordInput(string surfaceId, int revision, string contentHash, string correlationId,
        string causeKind, string causeId, IReadOnlyList<string> requiredClientCapabilities, JsonElement payload,
        IReadOnlyList<V2StoredActionBinding> actions, int protocolVersion, string surfaceSchema, int surfaceSchemaVersion,
        int actionSchemaVersion)
    {
        if (string.IsNullOrWhiteSpace(surfaceId) || surfaceId.Length > 256) throw new ArgumentException("Invalid V2 surface id.", nameof(surfaceId));
        if (string.IsNullOrWhiteSpace(contentHash)) throw new ArgumentException("A V2 surface content hash is required.", nameof(contentHash));
        if (requiredClientCapabilities is null) throw new ArgumentNullException(nameof(requiredClientCapabilities));
        if (actions is null) throw new ArgumentNullException(nameof(actions));
        if (string.IsNullOrWhiteSpace(surfaceSchema)) throw new ArgumentException("A V2 surface schema is required.", nameof(surfaceSchema));
        if (revision < 1) throw new ArgumentOutOfRangeException(nameof(revision));
        _ = NormalizeHash(contentHash);
        if (string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > 256) throw new ArgumentException("Invalid V2 correlation id.", nameof(correlationId));
        if (string.IsNullOrWhiteSpace(causeKind) || causeKind.Length > 64 || string.IsNullOrWhiteSpace(causeId) || causeId.Length > 256)
            throw new ArgumentException("A bounded V2 surface cause is required.");
        if (payload.ValueKind != JsonValueKind.Object) throw new ArgumentException("A V2 surface payload must be an object.", nameof(payload));
        V2SurfacePayloadPolicy.DemandSafe(payload);
        if (Encoding.UTF8.GetByteCount(payload.GetRawText()) > MaximumSurfacePayloadBytes)
            throw new ArgumentException("The V2 surface payload exceeds the durable delivery bound.", nameof(payload));
        if (requiredClientCapabilities.Count > MaximumCapabilitiesPerSurface || requiredClientCapabilities.Any(static capability =>
                string.IsNullOrWhiteSpace(capability) || capability.Length > 128))
            throw new ArgumentException("V2 surface capability requirements exceed the delivery bound.", nameof(requiredClientCapabilities));
        if (actions.Count > MaximumActionsPerSurface)
            throw new ArgumentException("V2 action bindings exceed the delivery bound.", nameof(actions));
        if (protocolVersion != V2UiProtocol.ProtocolVersion || !string.Equals(surfaceSchema, V2UiProtocol.SurfaceSchema, StringComparison.Ordinal) ||
            surfaceSchemaVersion != V2UiProtocol.SurfaceSchemaVersion || actionSchemaVersion != V2UiProtocol.ActionSchemaVersion)
            throw new ArgumentException("Unsupported durable V2 UI protocol or schema metadata.");
        if (actions.Any(static action => action is null))
            throw new ArgumentException("V2 action bindings cannot contain null records.", nameof(actions));
        if (actions.Select(static action => action.BindingId).Distinct(StringComparer.Ordinal).Count() != actions.Count)
            throw new ArgumentException("V2 action binding ids must be unique.", nameof(actions));
        foreach (var action in actions)
        {
            if (action is null || string.IsNullOrWhiteSpace(action.BindingId) || string.IsNullOrWhiteSpace(action.ActionType) ||
                string.IsNullOrWhiteSpace(action.RequiredGrant) || action.MaxUses < 1 || action.BindingId.Length > 256 ||
                action.ActionType.Length > 128 || string.IsNullOrWhiteSpace(action.InputSchemaRef) || action.InputSchemaRef.Length > 256 || action.RequiredGrant.Length > 128 ||
                action.ActionSchemaVersion != actionSchemaVersion || action.ExpiresAt == default)
                throw new ArgumentException("A complete V2 action binding is required.", nameof(actions));
        }
        var normalizedHash = NormalizeHash(contentHash);
        var recomputedHash = V2SurfaceContentHash.Compute(payload, actions);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(normalizedHash),
                Encoding.ASCII.GetBytes(recomputedHash)))
            throw new ArgumentException("The V2 surface content hash does not match its token-free content.", nameof(contentHash));
    }

    private static string NormalizeHash(string value)
    {
        var hash = value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? value[7..] : value;
        if (hash.Length != 64 || hash.Any(static c => !Uri.IsHexDigit(c))) throw new ArgumentException("A SHA-256 content hash is required.", nameof(value));
        return hash.ToLowerInvariant();
    }

    private static bool IsHash(string value)
    {
        var hash = value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? value[7..] : value;
        return hash.Length == 64 && hash.All(static c => Uri.IsHexDigit(c));
    }

    public static V2SurfaceAudience Audience(RequestContext context, V2SurfaceAudienceKind kind) => kind switch
    {
        V2SurfaceAudienceKind.Principal => new(kind, V2PrincipalScope.Id(context.Principal)),
        V2SurfaceAudienceKind.Workspace => new(kind, context.WorkspaceId.Value),
        V2SurfaceAudienceKind.Public => new(kind, string.Empty),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static FeedKey Key(RequestContext context, V2SurfaceAudienceKind kind)
    {
        var audience = Audience(context, kind);
        return new(context.TenantId.Value, context.WorkspaceId.Value, kind, audience.Id,
            kind == V2SurfaceAudienceKind.Principal ? context.Principal.Kind : null);
    }

    private static AckKey Ack(RequestContext context, V2SurfaceAudienceKind kind)
    {
        var key = Key(context, kind);
        return new(key, context.Principal.Value, context.Principal.Kind, context.SessionId);
    }

    private static string CursorNonce(RequestContext context, V2SurfaceAudienceKind kind, long sequence) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{context.TenantId}:{context.WorkspaceId}:{kind}:{sequence}")))[..16];

    private static TaskCompletionSource<long> NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class FeedState
    {
        public object Gate { get; } = new();
        public List<V2StoredSurfaceRecord> Items { get; } = [];
        public Dictionary<string, V2StoredSurfaceRecord> Current { get; } = new(StringComparer.Ordinal);
        public long LastSequence { get; set; }
        public TaskCompletionSource<long> Changed { get; set; } = NewSignal();
    }

    private readonly record struct FeedKey(
        string Tenant,
        string Workspace,
        V2SurfaceAudienceKind AudienceKind,
        string AudienceId,
        PrincipalKind? AudiencePrincipalKind);
    private readonly record struct AckKey(FeedKey Feed, string Principal, PrincipalKind PrincipalKind, string Session);
    private sealed record FeedLogEntry(
        string Kind,
        V2StoredSurfaceRecord? Record,
        string? Tenant,
        string? Workspace,
        V2SurfaceAudienceKind? AudienceKind,
        string? AudienceId,
        PrincipalKind? AudiencePrincipalKind,
        string? Principal,
        long MinimumSequence,
        string? Integrity = null);
}

/// <summary>Compatibility wrapper around the durable implementation.</summary>
public sealed class FileV2PrivateFeedStore : IV2PrivateFeedStore
{
    private readonly V2PrivateFeedStore _inner;

    public FileV2PrivateFeedStore(string root, byte[]? integrityKey = null) =>
        _inner = new V2PrivateFeedStore(Path.Combine(root, "v2-feeds", "feed.jsonl"), integrityKey: integrityKey);

    public V2StoredSurfaceRecord Append(RequestContext context, string surfaceId, int revision, string contentHash, JsonElement payload) =>
        _inner.Append(context, surfaceId, revision, contentHash, payload);
    public V2FeedPage CatchUp(RequestContext context, long? after, int limit = 50) => _inner.CatchUp(context, after, limit);
    public void RetainFrom(RequestContext context, long minimumSequence) => _inner.RetainFrom(context, minimumSequence);

    public V2StoredSurfaceRecord Append(RequestContext context, V2SurfaceAudienceKind audienceKind, string surfaceId, int revision,
        string contentHash, DateTimeOffset createdAt, DateTimeOffset? expiresAt, string correlationId, string causeKind,
        string causeId, IReadOnlyList<string> requiredClientCapabilities, JsonElement payload, IReadOnlyList<V2StoredActionBinding> actions,
        int protocolVersion = V2UiProtocol.ProtocolVersion, string surfaceSchema = V2UiProtocol.SurfaceSchema,
        int surfaceSchemaVersion = V2UiProtocol.SurfaceSchemaVersion, int actionSchemaVersion = V2UiProtocol.ActionSchemaVersion) =>
        _inner.Append(context, audienceKind, surfaceId, revision, contentHash, createdAt, expiresAt, correlationId, causeKind,
            causeId, requiredClientCapabilities, payload, actions, protocolVersion, surfaceSchema, surfaceSchemaVersion, actionSchemaVersion);
    public V2StoredSurfaceRecord EnsureInitial(RequestContext context, V2SurfaceAudienceKind audienceKind, string surfaceId, Func<long, V2StoredSurfaceRecord> factory) =>
        _inner.EnsureInitial(context, audienceKind, surfaceId, factory);
    public V2FeedPage CatchUp(RequestContext context, V2SurfaceAudienceKind audienceKind, long? after, int limit = 50) =>
        _inner.CatchUp(context, audienceKind, after, limit);
    public ValueTask WaitForChangeAsync(RequestContext context, V2SurfaceAudienceKind audienceKind, long after, CancellationToken cancellationToken) =>
        _inner.WaitForChangeAsync(context, audienceKind, after, cancellationToken);
    public void RetainFrom(RequestContext context, V2SurfaceAudienceKind audienceKind, long minimumSequence) =>
        _inner.RetainFrom(context, audienceKind, minimumSequence);
    public void MarkDelivered(RequestContext context, V2SurfaceAudienceKind audienceKind, long sequence) =>
        _inner.MarkDelivered(context, audienceKind, sequence);
    public ValueTask<bool> WaitUntilDeliveredAsync(RequestContext context, V2SurfaceAudienceKind audienceKind, long sequence, CancellationToken cancellationToken) =>
        _inner.WaitUntilDeliveredAsync(context, audienceKind, sequence, cancellationToken);
    public void Acknowledge(RequestContext context, V2SurfaceAudienceKind audienceKind, long sequence) =>
        _inner.Acknowledge(context, audienceKind, sequence);
    public long? Acknowledged(RequestContext context, V2SurfaceAudienceKind audienceKind) =>
        _inner.Acknowledged(context, audienceKind);
    public int? LatestRevision(RequestContext context, V2SurfaceAudienceKind audienceKind, string surfaceId) =>
        _inner.LatestRevision(context, audienceKind, surfaceId);
}

// Legacy registration shape retained for existing callers; live V2 UI uses Issue below with full owner scope.
public sealed record V2ActionBinding(string BindingId, string TemplateId, int TemplateVersion, string InputSchemaRef, int MaxUses, DateTimeOffset ExpiresAt, string IssuedTokenHash);
public sealed record V2UiActionUseRecord(string BindingId, string OperationId, string IdempotencyKey, DateTimeOffset UsedAt);
public sealed record V2ActionSubmission(string OperationId, string IdempotencyKey, JsonElement Input, string ActionType = "legacy");
public sealed record V2IssuedAction(string BindingId, string ActionType, string Token, DateTimeOffset ExpiresAt);

public enum V2ActionRejection
{
    Unavailable,
    Forged,
    Expired,
    Replay,
    WrongOwner,
    WrongWorkspace,
    WrongRevision,
    PolicyDenied
}

public sealed class V2ActionRejectedException(V2ActionRejection reason) : UnauthorizedAccessException("V2 action authorization failed.")
{
    public V2ActionRejection Reason { get; } = reason;
}

public sealed class V2ActionExecutor(IV2PrivateFeedStore? feed = null)
{
    private readonly SemaphoreSlim _linearization = new(1, 1);
    private readonly ConcurrentDictionary<string, IssuedBinding> _bindingsByTokenHash = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _uses = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<BindingOwnerKey, int> _bindingUses = new();
    private readonly ConcurrentDictionary<TokenOwnerKey, ConcurrentDictionary<string, DateTimeOffset>> _activeTokens = new();
    private readonly ConcurrentDictionary<string, V2UiActionUseRecord> _records = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<SurfaceOwnerKey, int> _latestRevisions = new();

    public void Register(V2ActionBinding binding)
    {
        if (!TryNormalizeHash(binding.IssuedTokenHash, out var hash)) throw new ArgumentException("Invalid action token hash.", nameof(binding));
        _bindingsByTokenHash[hash] = new IssuedBinding(binding.BindingId, "legacy", binding.InputSchemaRef, null, 0, null, null, null, null, null,
            string.Empty, binding.MaxUses, binding.ExpiresAt, hash, null, null, string.Empty, V2SurfaceAudienceKind.Principal, string.Empty);
    }

    public V2IssuedAction Issue(RequestContext recipient, V2StoredSurfaceRecord surface, V2StoredActionBinding binding, TimeSpan tokenLifetime)
    {
        if (surface.TenantId != recipient.TenantId || surface.WorkspaceId != recipient.WorkspaceId)
            throw new InvalidOperationException("Cannot issue an action outside the authenticated workspace.");
        if (surface.Audience.Kind != V2SurfaceAudienceKind.Principal ||
            !string.Equals(surface.Audience.Id, V2PrincipalScope.Id(recipient.Principal), StringComparison.Ordinal) ||
            surface.AudiencePrincipalKind != recipient.Principal.Kind)
            throw new V2ActionRejectedException(V2ActionRejection.PolicyDenied);
        if (tokenLifetime <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(tokenLifetime));
        var expires = new[] { binding.ExpiresAt, surface.ExpiresAt ?? DateTimeOffset.MaxValue, DateTimeOffset.UtcNow.Add(tokenLifetime) }.Min();
        if (expires <= DateTimeOffset.UtcNow) throw new V2ActionRejectedException(V2ActionRejection.Expired);
        var token = Base64Url(RandomNumberGenerator.GetBytes(32));
        var hash = Hash(token);
        var ownerKey = new BindingOwnerKey(recipient.TenantId.Value, recipient.WorkspaceId.Value, recipient.Principal.Value, recipient.Principal.Kind,
            surface.Audience.Kind, surface.Audience.Id, surface.SurfaceId, surface.Revision, binding.BindingId);
        var tokenOwnerKey = new TokenOwnerKey(ownerKey, recipient.SessionId);
        if (_bindingUses.TryGetValue(ownerKey, out var used) && used >= binding.MaxUses)
            throw new V2ActionRejectedException(V2ActionRejection.Replay);
        var ownerTokens = _activeTokens.GetOrAdd(tokenOwnerKey, static _ => new(StringComparer.Ordinal));
        foreach (var prior in ownerTokens.Where(static pair => pair.Value <= DateTimeOffset.UtcNow).ToArray())
        {
            ownerTokens.TryRemove(prior.Key, out _);
            _bindingsByTokenHash.TryRemove(prior.Key, out _);
            _uses.TryRemove(prior.Key, out _);
        }
        var issued = new IssuedBinding(binding.BindingId, binding.ActionType, binding.InputSchemaRef, surface.SurfaceId, surface.Revision,
            recipient.TenantId.Value, recipient.WorkspaceId.Value, recipient.Principal.Value, recipient.Principal.Kind, recipient.SessionId,
            binding.RequiredGrant, binding.MaxUses, expires, hash, ownerKey, tokenOwnerKey, surface.ContentHash,
            surface.Audience.Kind, surface.Audience.Id);
        _bindingsByTokenHash[hash] = issued;
        ownerTokens[hash] = expires;
        _latestRevisions.AddOrUpdate(new(recipient.TenantId.Value, recipient.WorkspaceId.Value, recipient.Principal.Value, recipient.Principal.Kind,
                surface.Audience.Kind, surface.Audience.Id, surface.SurfaceId),
            surface.Revision, (_, current) => Math.Max(current, surface.Revision));
        return new(binding.BindingId, binding.ActionType, token, expires);
    }

    public void NoteCurrentRevision(RequestContext context, V2SurfaceAudience audience, string surfaceId, int revision) =>
        _latestRevisions.AddOrUpdate(new(context.TenantId.Value, context.WorkspaceId.Value, context.Principal.Value, context.Principal.Kind,
                audience.Kind, audience.Id, surfaceId),
            revision, (_, current) => Math.Max(current, revision));

    public V2ActionSubmission Use(RequestContext context, string bindingId, string issuedToken, JsonElement input) =>
        Use(context, bindingId, issuedToken, null, 0, input);

    public V2ActionSubmission Use(RequestContext context, string bindingId, string issuedToken, string? surfaceId, int surfaceRevision, JsonElement input)
    {
        var authorization = ReserveAsync(context, bindingId, issuedToken, surfaceId, surfaceRevision, input, CancellationToken.None)
            .AsTask().GetAwaiter().GetResult();
        if (!Commit(authorization))
        {
            if (((IssuedBinding)authorization.BindingState).SurfaceId is null) throw new InvalidOperationException("V2 action binding usage limit exceeded.");
            throw new V2ActionRejectedException(V2ActionRejection.Replay);
        }
        return authorization.Submission;
    }

    public async ValueTask<Authorization> ReserveAsync(
        RequestContext context,
        string bindingId,
        string issuedToken,
        string? surfaceId,
        int surfaceRevision,
        JsonElement input,
        CancellationToken cancellationToken)
    {
        await _linearization.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var authorization = AuthorizeCore(context, bindingId, issuedToken, surfaceId, surfaceRevision, input);
            authorization.HoldsLinearization = true;
            var binding = (IssuedBinding)authorization.BindingState;
            var used = binding.OwnerKey is { } owner
                ? _bindingUses.GetValueOrDefault(owner)
                : _uses.GetValueOrDefault(binding.TokenHash);
            if (used >= binding.MaxUses && !authorization.IsReplay)
            {
                if (binding.SurfaceId is null) throw new InvalidOperationException("V2 action binding usage limit exceeded.");
                throw new V2ActionRejectedException(V2ActionRejection.Replay);
            }
            return authorization;
        }
        catch
        {
            _linearization.Release();
            throw;
        }
    }

    private Authorization AuthorizeCore(
        RequestContext context,
        string bindingId,
        string issuedToken,
        string? surfaceId,
        int surfaceRevision,
        JsonElement input)
    {
        if (string.IsNullOrWhiteSpace(issuedToken) || issuedToken.Length > 4096)
            throw new V2ActionRejectedException(V2ActionRejection.Forged);
        var hash = Hash(issuedToken);
        if (!_bindingsByTokenHash.TryGetValue(hash, out var binding) || !FixedTimeHashEquals(hash, binding.TokenHash) ||
            !string.Equals(binding.BindingId, bindingId, StringComparison.Ordinal))
            throw new V2ActionRejectedException(V2ActionRejection.Forged);
        if (binding.TokenOwnerKey is { } tokenOwnerKey &&
            (!_activeTokens.TryGetValue(tokenOwnerKey, out var activeHashes) || !activeHashes.ContainsKey(hash)))
            throw new V2ActionRejectedException(V2ActionRejection.Forged);
        if (binding.ExpiresAt <= DateTimeOffset.UtcNow) throw new V2ActionRejectedException(V2ActionRejection.Expired);
        if (binding.Tenant is not null && !string.Equals(binding.Tenant, context.TenantId.Value, StringComparison.Ordinal))
            throw new V2ActionRejectedException(V2ActionRejection.WrongWorkspace);
        if (binding.Workspace is not null && !string.Equals(binding.Workspace, context.WorkspaceId.Value, StringComparison.Ordinal))
            throw new V2ActionRejectedException(V2ActionRejection.WrongWorkspace);
        if (binding.Principal is not null && !string.Equals(binding.Principal, context.Principal.Value, StringComparison.Ordinal))
            throw new V2ActionRejectedException(V2ActionRejection.WrongOwner);
        if (binding.PrincipalKind is { } principalKind && principalKind != context.Principal.Kind)
            throw new V2ActionRejectedException(V2ActionRejection.WrongOwner);
        if (binding.Session is not null && !string.Equals(binding.Session, context.SessionId, StringComparison.Ordinal))
            throw new V2ActionRejectedException(V2ActionRejection.WrongOwner);
        if (!string.IsNullOrEmpty(binding.RequiredGrant) && !context.Grants.Contains(binding.RequiredGrant))
            throw new V2ActionRejectedException(V2ActionRejection.PolicyDenied);
        if (binding.SurfaceId is not null && (!string.Equals(binding.SurfaceId, surfaceId, StringComparison.Ordinal) || binding.Revision != surfaceRevision))
            throw new V2ActionRejectedException(V2ActionRejection.WrongRevision);
        var idempotency = binding.SurfaceId is null
            ? $"v2-action-{Guid.NewGuid():N}"
            : StableIdempotency(binding);
        var recordedReplay = binding.SurfaceId is not null && _records.ContainsKey(idempotency);
        if (!recordedReplay && binding.SurfaceId is not null && binding.OwnerKey is { } revisionOwner && _latestRevisions.TryGetValue(
                new(context.TenantId.Value, context.WorkspaceId.Value, context.Principal.Value, context.Principal.Kind,
                    revisionOwner.AudienceKind, revisionOwner.AudienceId, binding.SurfaceId), out var latest) &&
            latest != binding.Revision)
            throw new V2ActionRejectedException(V2ActionRejection.WrongRevision);
        if (!recordedReplay && binding.SurfaceId is not null && feed is not null &&
            feed.LatestRevision(context, binding.AudienceKind, binding.SurfaceId) != binding.Revision)
            throw new V2ActionRejectedException(V2ActionRejection.WrongRevision);
        if (binding.SurfaceId is not null) DemandInputSchema(binding.InputSchemaRef, input);

        var operationId = $"v2-op-{Guid.NewGuid():N}";
        return new Authorization(binding,
            new V2ActionSubmission(operationId, idempotency, input.Clone(), binding.ActionType))
        {
            IsReplay = recordedReplay
        };
    }

    public bool Commit(Authorization authorization, string? durableOperationId = null)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        if (Interlocked.Exchange(ref authorization.Completed, 1) != 0)
            throw new InvalidOperationException("The V2 action authorization has already completed.");
        try
        {
            var binding = (IssuedBinding)authorization.BindingState;
            var submission = authorization.Submission;
            if (authorization.IsReplay)
                return durableOperationId is not null &&
                       _records.TryGetValue(submission.IdempotencyKey, out var recorded) &&
                       string.Equals(recorded.OperationId, durableOperationId, StringComparison.Ordinal);
            var hash = binding.TokenHash;
            var use = binding.OwnerKey is { } bindingOwner
                ? _bindingUses.AddOrUpdate(bindingOwner, 1, static (_, current) => checked(current + 1))
                : _uses.AddOrUpdate(hash, 1, static (_, current) => checked(current + 1));
            if (use > binding.MaxUses)
            {
                if (binding.OwnerKey is { } owner)
                    _bindingUses.AddOrUpdate(owner, 0, static (_, current) => Math.Max(0, current - 1));
                else
                    _uses.AddOrUpdate(hash, 0, static (_, current) => Math.Max(0, current - 1));
                return false;
            }
            _records[submission.IdempotencyKey] = new V2UiActionUseRecord(
                binding.BindingId, durableOperationId ?? submission.OperationId, submission.IdempotencyKey, DateTimeOffset.UtcNow);
            return true;
        }
        finally { ReleaseLinearization(authorization); }
    }

    public void Release(Authorization authorization)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        if (Interlocked.Exchange(ref authorization.Completed, 1) == 0) ReleaseLinearization(authorization);
    }

    public IDisposable EnterSurfaceMutation()
    {
        _linearization.Wait();
        return new LinearizationLease(_linearization);
    }

    private void ReleaseLinearization(Authorization authorization)
    {
        if (authorization.HoldsLinearization)
        {
            authorization.HoldsLinearization = false;
            _linearization.Release();
        }
    }

    public bool TryGetUse(string idempotencyKey, out V2UiActionUseRecord? record) => _records.TryGetValue(idempotencyKey, out record);

    public sealed class Authorization
    {
        internal Authorization(object bindingState, V2ActionSubmission submission)
        {
            BindingState = bindingState;
            Submission = submission;
        }

        internal object BindingState { get; }
        public V2ActionSubmission Submission { get; }
        internal bool HoldsLinearization { get; set; }
        internal bool IsReplay { get; set; }
        internal int Completed;
    }

    private sealed class LinearizationLease(SemaphoreSlim gate) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) gate.Release();
        }
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static bool TryNormalizeHash(string value, out string hash)
    {
        hash = value.ToUpperInvariant();
        return hash.Length == 64 && hash.All(static c => Uri.IsHexDigit(c));
    }
    private static bool FixedTimeHashEquals(string first, string second) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(first), Encoding.ASCII.GetBytes(second));
    private static string StableIdempotency(IssuedBinding binding)
    {
        var canonical = JsonSerializer.SerializeToUtf8Bytes(new
        {
            binding.Tenant,
            binding.Workspace,
            binding.Principal,
            principalKind = binding.PrincipalKind is null ? (int?)null : (int)binding.PrincipalKind.Value,
            audienceKind = (int)binding.AudienceKind,
            binding.AudienceId,
            binding.SurfaceId,
            binding.Revision,
            binding.BindingId,
            binding.ActionType,
            binding.ContentHash
        });
        return "v2-ui-action-" + Convert.ToHexString(SHA256.HashData(canonical)).ToLowerInvariant();
    }
    private static void DemandInputSchema(string schemaRef, JsonElement input)
    {
        if (string.Equals(schemaRef, "digitalbrain.ui.refresh-input.v1", StringComparison.Ordinal) &&
            input.ValueKind == JsonValueKind.Object && !input.EnumerateObject().Any())
            return;
        if (string.Equals(schemaRef, V2WorkspaceSurfaceProducer.InoInputSchema, StringComparison.Ordinal) &&
            input.ValueKind == JsonValueKind.Object && input.EnumerateObject().Count() == 1 &&
            input.TryGetProperty("prompt", out var prompt) && prompt.ValueKind == JsonValueKind.String &&
            prompt.GetString() is { } value && !string.IsNullOrWhiteSpace(value) && value.Length <= 4096)
            return;
        throw new V2ActionRejectedException(V2ActionRejection.PolicyDenied);
    }

    private sealed record IssuedBinding(
        string BindingId,
        string ActionType,
        string InputSchemaRef,
        string? SurfaceId,
        int Revision,
        string? Tenant,
        string? Workspace,
        string? Principal,
        PrincipalKind? PrincipalKind,
        string? Session,
        string RequiredGrant,
        int MaxUses,
        DateTimeOffset ExpiresAt,
        string TokenHash,
        BindingOwnerKey? OwnerKey,
        TokenOwnerKey? TokenOwnerKey,
        string ContentHash,
        V2SurfaceAudienceKind AudienceKind,
        string AudienceId);
    private readonly record struct SurfaceOwnerKey(string Tenant, string Workspace, string Principal, PrincipalKind PrincipalKind,
        V2SurfaceAudienceKind AudienceKind, string AudienceId, string SurfaceId);
    private readonly record struct BindingOwnerKey(string Tenant, string Workspace, string Principal, PrincipalKind PrincipalKind,
        V2SurfaceAudienceKind AudienceKind, string AudienceId, string SurfaceId, int Revision, string BindingId);
    private readonly record struct TokenOwnerKey(BindingOwnerKey Binding, string Session);
}
