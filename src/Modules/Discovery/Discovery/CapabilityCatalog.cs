using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Discovery.Search;
using DigitalBrain.Qdrant;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Discovery;

// Hybrid search over every capability source: keyword overlap for precision, Qdrant vectors for
// meaning. Without an embedding model it is keyword search alone. Sources are the truth; the
// catalog re-reads them only when a change invalidates it.
internal sealed class CapabilityCatalog(
    IEnumerable<ICapabilitySource> sources,
    IQdrant qdrant,
    IEmbeddingGenerator<string, Embedding<float>>? embeddings,
    ILogger<CapabilityCatalog> logger)
{
    private const double MatchFloor = 0.45;
    private const double SemanticFloor = 0.5;
    private const double VectorBonus = 0.3;
    private const int VectorWindow = 16;
    private const string KeyField = "key";
    private const string WorkspaceField = "workspace";
    private const string GroupField = "group";
    private const string AppGroup = "app";
    private const string OtherGroup = "other";

    private readonly ICapabilitySource[] _sources = [.. sources];
    private readonly Dictionary<ICapabilitySource, IReadOnlyList<CapabilityDocument>> _lastRead = [];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Snapshot _snapshot = Snapshot.Empty;
    private volatile bool _dirty = true;

    public void Invalidate() => _dirty = true;

    public async Task RebuildAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var sourceFailed = false;
            foreach (var source in _sources)
            {
                try { _lastRead[source] = await source.Read(cancellationToken).ConfigureAwait(false); }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // Keep what this source returned last time; retry on the next search.
                    logger.LogWarning(exception, "Capabilities from {Source} could not be read; searching the rest.", source.GetType().Name);
                    sourceFailed = true;
                }
            }
            _dirty = sourceFailed;

            var documents = _lastRead.Values.SelectMany(static read => read)
                .GroupBy(Key, StringComparer.Ordinal)
                .ToDictionary(static group => group.Key, static group => group.Last(), StringComparer.Ordinal);
            var signature = Signature(documents.Values);
            if (signature == _snapshot.Signature && (_snapshot.VectorsReady || embeddings is null))
            {
                _snapshot = _snapshot with { SourceFailed = sourceFailed };
                return;
            }

            var collection = await StoreVectorsAsync(documents, cancellationToken).ConfigureAwait(false);
            _snapshot = new(new KeywordIndex([.. documents.Values]), documents, collection, signature, sourceFailed, embeddings is not null);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<CapabilitySearchResult> SearchAsync(string query, string? workspaceId, int take, CancellationToken cancellationToken, bool appsOnly = false)
    {
        if (_dirty) { await RebuildAsync(cancellationToken).ConfigureAwait(false); }
        var snapshot = _snapshot;
        var degraded = snapshot.Degraded;
        if (string.IsNullOrWhiteSpace(query) || take < 1) { return new() { Degraded = degraded }; }

        bool Visible(CapabilityDocument document)
            => (document.WorkspaceId is null || document.WorkspaceId == workspaceId)
                && (!appsOnly || document.Kind is CapabilityKind.App or CapabilityKind.Operation);

        if (snapshot.Keywords.Alias(query, Visible) is { } alias) { return new() { Hits = [Hit(alias, 1d)], Degraded = degraded }; }

        var keyword = snapshot.Keywords.Score(query, Visible).ToDictionary(static match => Key(match.Document), static match => match.Score, StringComparer.Ordinal);
        var semantic = new Dictionary<string, double>(StringComparer.Ordinal);
        if (snapshot.VectorsReady)
        {
            try { semantic = await SemanticScoresAsync(query, workspaceId, appsOnly, snapshot, cancellationToken).ConfigureAwait(false); }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Vector search failed; discovery answers from keywords only.");
                degraded = true;
            }
        }

        // A keyword match is kept on its own merit and vectors only reorder it; a capability without
        // enough shared words needs a strong vector match to appear at all.
        var hits = keyword.Keys.Union(semantic.Keys)
            .Select(key => (Document: snapshot.Documents[key], Score: keyword.GetValueOrDefault(key) >= MatchFloor
                ? keyword[key] + VectorBonus * Math.Clamp(semantic.GetValueOrDefault(key), 0d, 1d)
                : semantic.GetValueOrDefault(key) >= SemanticFloor ? semantic[key] : 0d))
            .Where(match => match.Score > 0 && Visible(match.Document))
            .OrderByDescending(static match => match.Score)
            .ThenBy(static match => match.Document.Id, StringComparer.Ordinal)
            .Take(take)
            .Select(static match => Hit(match.Document, match.Score))
            .ToArray();
        return new() { Hits = hits, Degraded = degraded };
    }

    private async Task<Dictionary<string, double>> SemanticScoresAsync(string query, string? workspaceId, bool appsOnly,
        Snapshot snapshot, CancellationToken cancellationToken)
    {
        var vector = (await embeddings!.GenerateAsync([query], cancellationToken: cancellationToken).ConfigureAwait(false))[0].Vector.ToArray();
        var scores = new Dictionary<string, double>(StringComparer.Ordinal);
        string[] scopes = workspaceId is null ? [""] : ["", workspaceId];
        foreach (var scope in scopes)
        {
            var filter = new Dictionary<string, string>(StringComparer.Ordinal) { [WorkspaceField] = scope };
            if (appsOnly) { filter[GroupField] = AppGroup; }
            foreach (var hit in await qdrant.Search(snapshot.Collection!, vector, VectorWindow, filter, cancellationToken).ConfigureAwait(false))
            {
                // Points left by capabilities that no longer exist are ignored until the next rebuild removes them.
                if (hit.Payload.TryGetValue(KeyField, out var key) && snapshot.Documents.ContainsKey(key))
                { scores[key] = Math.Max(scores.GetValueOrDefault(key), hit.Score); }
            }
        }
        return scores;
    }

    private async Task<string?> StoreVectorsAsync(Dictionary<string, CapabilityDocument> documents, CancellationToken cancellationToken)
    {
        if (documents.Count == 0 || embeddings is null) { return null; }
        try
        {
            var ordered = documents.ToArray();
            var vectors = await embeddings.GenerateAsync(ordered.Select(static pair => EmbeddingText(pair.Value)), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var collection = "capabilities_" + vectors[0].Vector.Length;
            await qdrant.Upsert(collection, [.. ordered.Select((pair, index) =>
                new VectorPoint(pair.Key, vectors[index].Vector.ToArray(), Payload(pair.Key, pair.Value)))], cancellationToken).ConfigureAwait(false);
            if (collection == _snapshot.Collection)
            {
                var removed = _snapshot.Documents.Keys.Where(key => !documents.ContainsKey(key)).ToArray();
                await qdrant.Delete(collection, removed, cancellationToken).ConfigureAwait(false);
            }
            return collection;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Capability vectors could not be stored; discovery answers from keywords only.");
            return null;
        }
    }

    private static string Key(CapabilityDocument document)
        => document.WorkspaceId is null ? document.Id : document.WorkspaceId + "\0" + document.Id;

    private static string EmbeddingText(CapabilityDocument document) => document.Name + ": " + document.Description;

    private static Dictionary<string, string> Payload(string key, CapabilityDocument document) => new(StringComparer.Ordinal)
    {
        [KeyField] = key,
        [WorkspaceField] = document.WorkspaceId ?? "",
        [GroupField] = document.Kind is CapabilityKind.App or CapabilityKind.Operation ? AppGroup : OtherGroup,
        ["id"] = document.Id,
        ["kind"] = document.Kind.ToString(),
        ["name"] = document.Name,
    };

    private static CapabilityHit Hit(CapabilityDocument document, double score) => new()
    {
        Id = document.Id,
        Kind = document.Kind,
        Score = Math.Round(score, 4),
        Name = document.Name,
        Description = document.Description,
    };

    private static string Signature(IEnumerable<CapabilityDocument> documents)
    {
        var builder = new StringBuilder();
        foreach (var document in documents.OrderBy(Key, StringComparer.Ordinal))
        {
            builder.Append(Key(document)).Append('|').Append(document.Kind).Append('|')
                .Append(document.Name).Append('|').Append(document.Description).Append(';');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private sealed record Snapshot(
        KeywordIndex Keywords,
        IReadOnlyDictionary<string, CapabilityDocument> Documents,
        string? Collection,
        string Signature,
        bool SourceFailed,
        bool VectorsExpected)
    {
        public static Snapshot Empty { get; } = new(KeywordIndex.Empty, new Dictionary<string, CapabilityDocument>(), null, "", false, false);

        public bool VectorsReady => Collection is not null;

        public bool Degraded => SourceFailed || (VectorsExpected && Documents.Count > 0 && !VectorsReady);
    }
}
