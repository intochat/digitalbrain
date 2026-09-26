using System.Text;
using System.Text.Json;

namespace DigitalBrain.Coding;

internal sealed class CodeDraftStore(string root)
{
    private readonly DurableDocumentStore<CodeDraftDocument> _documents = new(root, () => new());
    public Task<CodeDraftSnapshot> ReadAsync(string id, CancellationToken ct)
        => _documents.ReadAsync(id, d => d.Current, ct);

    public Task<CodeDraftSnapshot> SaveAsync(string id, SaveCodeDraft request, CancellationToken ct)
    {
        DraftRules.Validate(request);
        return _documents.UpdateAsync(id, document =>
        {
            var hash = ArtifactStore.Hash(JsonSerializer.SerializeToUtf8Bytes(request));
            if (document.Saves.TryGetValue(request.OperationId, out var prior))
            {
                if (prior.Hash != hash) { throw new InvalidOperationException("Operation ID was used with different input."); }
                return prior.Snapshot;
            }
            if (document.Checks.ContainsKey(request.OperationId)) { throw new InvalidOperationException("Operation ID is already used by a check."); }
            if (document.Current.Revision != request.ExpectedRevision) { throw new InvalidOperationException("Draft revision conflict; read the draft again."); }
            if (document.Saves.Count >= 128) { throw new InvalidOperationException("Draft revision limit reached; create a new draft."); }
            var next = new CodeDraftSnapshot(request.ExpectedRevision + 1, request.Source, request.Tests, request.ModuleIds.ToArray(), null);
            document.Id = id;
            document.Saves.Add(request.OperationId, new(hash, next));
            document.Current = next;
            return next;
        }, ct);
    }

    public Task<CodeCheckSnapshot> QueueAsync(string id, CheckCodeDraft request, CancellationToken ct)
    {
        if (request.OperationId == Guid.Empty) { throw new ArgumentException("Operation ID is required.", nameof(request)); }
        return _documents.UpdateAsync(id, document =>
        {
            if (document.Checks.TryGetValue(request.OperationId, out var previous))
            {
                if (previous.Revision != request.Revision) { throw new InvalidOperationException("Operation ID was used with different input."); }
                return previous;
            }
            if (document.Saves.ContainsKey(request.OperationId)) { throw new InvalidOperationException("Operation ID is already used by a save."); }
            if (request.Revision <= 0 || document.Current.Revision != request.Revision) { throw new InvalidOperationException("Check requires the current draft revision."); }
            if (document.Checks.Count >= 256) { throw new InvalidOperationException("Check limit reached; create a new draft."); }
            var check = new CodeCheckSnapshot(request.OperationId, request.Revision, CodeCheckStatus.Queued, [], null, DateTimeOffset.UtcNow, null, null);
            document.Checks.Add(request.OperationId, check);
            document.Inputs.Add(request.OperationId, document.Current);
            document.Current = document.Current with { LatestCheckId = request.OperationId };
            return check;
        }, ct);
    }

    public Task<CodeCheckSnapshot> ReadCheckAsync(string id, Guid operation, CancellationToken ct)
        => _documents.ReadAsync(id, d => d.Checks.TryGetValue(operation, out var check) ? check : throw new KeyNotFoundException("Check not found."), ct);

    public Task<CodeDraftSnapshot> ReadInputAsync(string id, Guid operation, CancellationToken ct)
        => _documents.ReadAsync(id, d => d.Inputs[operation], ct);

    public Task<CodeCheckSnapshot> UpdateCheckAsync(string id, Guid operation, Func<CodeCheckSnapshot, CodeCheckSnapshot> update, CancellationToken ct)
        => _documents.UpdateAsync(id, d =>
        {
            var current = d.Checks[operation];
            if (IsTerminal(current.Status)) { return current; }
            return d.Checks[operation] = update(current);
        }, ct);

    public async Task InterruptPendingAsync(CancellationToken ct)
    {
        foreach (var id in await _documents.ListIdsAsync(ct).ConfigureAwait(false))
        {
            await _documents.UpdateAsync(id, d =>
            {
                foreach (var check in d.Checks.Values.ToArray())
                {
                    if (!IsTerminal(check.Status)) { d.Checks[check.OperationId] = check with { Status = CodeCheckStatus.Interrupted, CompletedAt = DateTimeOffset.UtcNow }; }
                }
                return true;
            }, ct).ConfigureAwait(false);
        }
    }

    internal static bool IsTerminal(CodeCheckStatus status) => status is CodeCheckStatus.Passed or CodeCheckStatus.Failed or CodeCheckStatus.Cancelled or CodeCheckStatus.Interrupted;
}

internal sealed class CodeDraftDocument
{
    public string Id { get; set; } = "";
    public CodeDraftSnapshot Current { get; set; } = new(0, "", "", [], null);
    public Dictionary<Guid, CodeDraftSave> Saves { get; set; } = [];
    public Dictionary<Guid, CodeCheckSnapshot> Checks { get; set; } = [];
    public Dictionary<Guid, CodeDraftSnapshot> Inputs { get; set; } = [];
}

internal sealed record CodeDraftSave(string Hash, CodeDraftSnapshot Snapshot);

// Cross-instance lock files and atomic replacement also protect supervisor recovery against overlapping host instances.
public sealed class DurableDocumentStore<T>(string root, Func<T> create) where T : class
{
    private readonly string _root = Path.GetFullPath(root);

    public Task<TResult> ReadAsync<TResult>(string id, Func<T, TResult> read, CancellationToken ct)
        => AccessAsync(id, read, false, ct);
    public Task<TResult> UpdateAsync<TResult>(string id, Func<T, TResult> update, CancellationToken ct)
        => AccessAsync(id, update, true, ct);

    public async Task<IReadOnlyList<string>> ListIdsAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(_root);
        List<string> ids = [];
        foreach (var file in Directory.EnumerateFiles(_root, "*.json"))
        {
            await using var lease = await LockAsync(Path.ChangeExtension(file, ".lock"), ct).ConfigureAwait(false);
            await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            ids.Add(json.RootElement.GetProperty("Id").GetString()!);
        }
        return ids;
    }

    private async Task<TResult> AccessAsync<TResult>(string id, Func<T, TResult> action, bool write, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (Encoding.UTF8.GetByteCount(id) > 1024) { throw new ArgumentException("Document identity is too long.", nameof(id)); }
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, ArtifactStore.Hash(Encoding.UTF8.GetBytes(id)));
        await using var lease = await LockAsync(path + ".lock", ct).ConfigureAwait(false);
        var jsonPath = path + ".json";
        T document;
        if (File.Exists(jsonPath))
        {
            if (new FileInfo(jsonPath).Length > 64 * 1024 * 1024) { throw new InvalidDataException("Document exceeds storage limit."); }
            document = JsonSerializer.Deserialize<T>(await File.ReadAllBytesAsync(jsonPath, ct).ConfigureAwait(false)) ?? throw new InvalidDataException("Invalid persisted document.");
        }
        else { document = create(); }
        var result = action(document);
        if (!write) { return result; }
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document);
        if (bytes.Length > 64 * 1024 * 1024) { throw new InvalidDataException("Document exceeds storage limit."); }
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, jsonPath, overwrite: true);
            return result;
        }
        finally { if (File.Exists(temporary)) { File.Delete(temporary); } }
    }

    private static async Task<FileStream> LockAsync(string path, CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException)
            { await Task.Delay(20, ct).ConfigureAwait(false); }
        }
    }
}