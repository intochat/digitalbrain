using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Coding;
using DigitalBrain.Core;
using Orleans;

namespace IntoChat;

internal sealed record DescribeBehavior(long ExpectedRevision, string Name, string Purpose, string[] Triggers, string[] Effects);
internal sealed record BehaviorDescription(string Id, long Revision, string Name, string Purpose, string[] Triggers, string[] Effects, DateTimeOffset CreatedAt);

// Human-facing identity belongs to the application. Execution remains owned by IBehaviorProgram.
internal sealed class BehaviorCatalogStore
{
    private readonly IDocumentStore<BehaviorCatalogIndex> _indexes;
    private readonly IDocumentStore<BehaviorCatalogDocument> _items;
    public BehaviorCatalogStore(IGrainFactory grains)
        : this(new GrainDocumentStore<BehaviorCatalogIndex>(grains, "behavior-catalog-index"),
            new GrainDocumentStore<BehaviorCatalogDocument>(grains, "behavior-catalog-items")) { }
    internal BehaviorCatalogStore(IDocumentStore<BehaviorCatalogIndex> indexes, IDocumentStore<BehaviorCatalogDocument> items)
    {
        _indexes = indexes;
        _items = items;
    }

    public async Task Register(string scope, string id, CancellationToken ct)
    {
        var key = BehaviorToolScope.Key(scope, id);
        await _items.UpdateAsync(key, doc => doc.Description ??= new(id, 0, id, "", [], [], DateTimeOffset.UtcNow), ct);
        await _indexes.UpdateAsync(scope, doc =>
        {
            if (!doc.Ids.Contains(id) && doc.Ids.Count >= 500) { throw new InvalidOperationException("This workspace has reached its 500-behavior catalog limit."); }
            doc.Id = scope;
            return doc.Ids.Add(id);
        }, ct);
    }

    public async Task<IReadOnlyList<BehaviorDescription>> List(string scope, CancellationToken ct)
    {
        var ids = await _indexes.ReadAsync(scope, doc => doc.Ids.ToArray(), ct);
        var items = new List<BehaviorDescription>();
        foreach (var id in ids) { items.Add(await Read(scope, id, ct)); }
        return items.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public Task<BehaviorDescription> Read(string scope, string id, CancellationToken ct)
        => _items.ReadAsync(BehaviorToolScope.Key(scope, id), doc => doc.Description ?? throw new KeyNotFoundException("Behavior is not in this workspace catalog."), ct);

    public async Task<BehaviorDescription> Describe(string scope, string id, DescribeBehavior request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 120 || request.Purpose is null || request.Purpose.Length > 4000
            || request.Triggers is null || request.Effects is null || request.Triggers.Length > 16 || request.Effects.Length > 16
            || request.Triggers.Concat(request.Effects).Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 240))
        { throw new ArgumentException("Supply a name (1–120 characters), purpose (up to 4000), and up to 16 short trigger/effect descriptions."); }
        await Register(scope, id, ct);
        return await _items.UpdateAsync(BehaviorToolScope.Key(scope, id), doc =>
        {
            var current = doc.Description!;
            if (current.Revision != request.ExpectedRevision) { throw new InvalidOperationException("Behavior details changed. Refresh before editing again."); }
            return doc.Description = current with { Revision = current.Revision + 1, Name = request.Name.Trim(), Purpose = request.Purpose.Trim(), Triggers = request.Triggers, Effects = request.Effects };
        }, ct);
    }

    public async Task Remove(string scope, string id, CancellationToken ct)
    {
        await _items.UpdateAsync(BehaviorToolScope.Key(scope, id), doc =>
        {
            doc.Description = null;
            doc.CheckedSources.Clear();
            return true;
        }, ct);
        await _indexes.UpdateAsync(scope, doc =>
        {
            doc.Id = scope;
            return doc.Ids.Remove(id);
        }, ct);
    }

    public async Task RememberChecked(string scope, string id, CodeDraftSnapshot draft, CodeCheckSnapshot? check, CancellationToken ct)
    {
        if (check?.Status != CodeCheckStatus.Passed || check.Artifact is not { } artifact || check.Revision != draft.Revision
            || artifact.SourceHash != Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(draft.Source)))) { return; }
        var key = BehaviorToolScope.Key(scope, id);
        if (await _items.ReadAsync(key, doc => doc.CheckedSources.ContainsKey(artifact.Id), ct)) { return; }
        await _items.UpdateAsync(key, doc =>
        {
            if (doc.CheckedSources.Count >= 128) { return false; }
            doc.CheckedSources.TryAdd(artifact.Id, draft);
            return true;
        }, ct);
    }

    public Task<CodeDraftSnapshot?> CheckedSource(string scope, string id, string artifactId, CancellationToken ct)
        => _items.ReadAsync(BehaviorToolScope.Key(scope, id), doc => doc.CheckedSources.GetValueOrDefault(artifactId), ct);
}

internal sealed class BehaviorCatalogIndex
{
    public string Id { get; set; } = "";
    public HashSet<string> Ids { get; set; } = new(StringComparer.Ordinal);
}
internal sealed class BehaviorCatalogDocument
{
    public BehaviorDescription? Description { get; set; }
    public Dictionary<string, CodeDraftSnapshot> CheckedSources { get; set; } = new(StringComparer.Ordinal);
}