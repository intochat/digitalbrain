using System.Text.Json;
using DigitalBrain.Core;

namespace DigitalBrain.Coding;

internal sealed class CodeDraftStore(IDocumentStore<CodeDraftDocument> documents)
{
    public Task<CodeDraftSnapshot> ReadAsync(string id, CancellationToken ct)
        => documents.ReadAsync(id, d => d.Current, ct);

    public Task<CodeDraftSnapshot> SaveAsync(string id, SaveCodeDraft request, CancellationToken ct)
    {
        DraftRules.Validate(request);
        return documents.UpdateAsync(id, document =>
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
        return documents.UpdateAsync(id, document =>
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
        => documents.ReadAsync(id, d => d.Checks.TryGetValue(operation, out var check) ? check : throw new KeyNotFoundException("Check not found."), ct);

    public Task<CodeDraftSnapshot> ReadInputAsync(string id, Guid operation, CancellationToken ct)
        => documents.ReadAsync(id, d => d.Inputs[operation], ct);

    public Task<CodeCheckSnapshot> UpdateCheckAsync(string id, Guid operation, Func<CodeCheckSnapshot, CodeCheckSnapshot> update, CancellationToken ct)
        => documents.UpdateAsync(id, d =>
        {
            var current = d.Checks[operation];
            if (IsTerminal(current.Status)) { return current; }
            return d.Checks[operation] = update(current);
        }, ct);

    public async Task InterruptPendingAsync(CancellationToken ct)
    {
        foreach (var id in await documents.ListIdsAsync(ct).ConfigureAwait(false))
        {
            await documents.UpdateAsync(id, d =>
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
