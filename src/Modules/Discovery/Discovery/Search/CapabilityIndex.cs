using DigitalBrain.Apps;

namespace DigitalBrain.Discovery.Search;

internal sealed record IndexedCapability(
    string Id,
    CapabilityKind Kind,
    string Name,
    IReadOnlyList<string> Aliases,
    IReadOnlySet<string> Tokens,
    float[]? Embedding);

internal sealed class CapabilityIndex
{
    private const double KeywordFloor = 0.45;
    private const int VectorCandidateWindow = 8;
    private const double VectorBonus = 0.3;

    private readonly IReadOnlyList<IndexedCapability> _entries;
    private readonly Dictionary<string, double> _inverseDocumentFrequency;
    private readonly Dictionary<string, IndexedCapability> _aliases;
    private readonly Func<string, CancellationToken, ValueTask<float[]?>>? _embed;

    private CapabilityIndex(
        IReadOnlyList<IndexedCapability> entries,
        Dictionary<string, double> inverseDocumentFrequency,
        Dictionary<string, IndexedCapability> aliases,
        Func<string, CancellationToken, ValueTask<float[]?>>? embed)
    {
        _entries = entries;
        _inverseDocumentFrequency = inverseDocumentFrequency;
        _aliases = aliases;
        _embed = embed;
    }

    public static CapabilityIndex Empty { get; } =
        new([], new(StringComparer.Ordinal), new(StringComparer.OrdinalIgnoreCase), null);

    public static async Task<CapabilityIndex> BuildAsync(
        IReadOnlyList<AppManifest> manifests,
        Func<string, CancellationToken, ValueTask<float[]?>>? embed,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifests);
        var entries = new List<IndexedCapability>();
        var aliases = new Dictionary<string, IndexedCapability>(StringComparer.OrdinalIgnoreCase);

        foreach (var manifest in manifests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var appText = string.Join(' ', manifest.Name, manifest.DescriptionForPeople, manifest.DescriptionForModel, manifest.Id);
            var appEntry = new IndexedCapability(
                manifest.Id,
                CapabilityKind.App,
                manifest.Name,
                [manifest.Id],
                Tokenize(appText),
                await EmbedAsync(embed, appText, cancellationToken).ConfigureAwait(false));
            entries.Add(appEntry);
            aliases[manifest.Id] = appEntry;
            aliases[manifest.Id.Replace('.', ' ')] = appEntry;

            foreach (var operation in manifest.Operations)
            {
                var operationText = string.Join(' ', operation.Name, operation.DescriptionForModel);
                entries.Add(new IndexedCapability(
                    manifest.Id + "/" + operation.Name,
                    CapabilityKind.Operation,
                    operation.Name,
                    [],
                    Tokenize(operationText),
                    await EmbedAsync(embed, operationText, cancellationToken).ConfigureAwait(false)));
            }
        }

        return new CapabilityIndex(entries, BuildIdf(entries), aliases, embed);
    }

    public async ValueTask<CapabilitySearchResult> SearchAsync(string query, int take, bool degraded, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query) || take < 1 || _entries.Count == 0)
        {
            return new CapabilitySearchResult { Degraded = degraded };
        }

        if (_aliases.TryGetValue(query.Trim(), out var aliasMatch))
        {
            return new CapabilitySearchResult { Hits = [Hit(aliasMatch, 1d)], Degraded = degraded };
        }

        var queryTokens = TextTokens.Split(query).Distinct(StringComparer.Ordinal).ToArray();
        var totalWeight = queryTokens.Sum(TokenIdf);
        if (totalWeight <= 0)
        {
            return new CapabilitySearchResult { Degraded = degraded };
        }

        var scored = new List<(IndexedCapability Entry, double Keyword)>();
        foreach (var entry in _entries)
        {
            var matched = queryTokens.Where(entry.Tokens.Contains).Sum(TokenIdf);
            if (matched > 0)
            {
                scored.Add((entry, matched / totalWeight));
            }
        }

        float[]? queryEmbedding = null;
        if (!degraded && scored.Count > VectorCandidateWindow && _embed is not null
            && _entries.Any(static entry => entry.Embedding is not null))
        {
            queryEmbedding = await _embed(query, cancellationToken).ConfigureAwait(false);
        }

        var hits = scored
            .Select(pair =>
            {
                var score = pair.Keyword;
                if (queryEmbedding is not null && pair.Entry.Embedding is not null)
                {
                    score += VectorBonus * Math.Clamp(HashingCapabilityEmbedder.Cosine(pair.Entry.Embedding, queryEmbedding), 0f, 1f);
                }

                return (pair.Entry, Score: score);
            })
            .Where(static pair => pair.Score >= KeywordFloor)
            .OrderByDescending(static pair => pair.Score)
            .ThenBy(static pair => pair.Entry.Id, StringComparer.Ordinal)
            .Take(take)
            .Select(static pair => Hit(pair.Entry, pair.Score))
            .ToArray();

        return new CapabilitySearchResult { Hits = hits, Degraded = degraded };
    }

    private static CapabilityHit Hit(IndexedCapability entry, double score)
        => new() { Id = entry.Id, Kind = entry.Kind, Score = Math.Round(score, 4) };

    public IReadOnlyList<(string Id, string Text, float[] Embedding)> EmbeddedEntries()
        => _entries
            .Where(static entry => entry.Embedding is not null)
            .Select(static entry => (entry.Id, entry.Name, entry.Embedding!))
            .ToArray();

    private static async ValueTask<float[]?> EmbedAsync(
        Func<string, CancellationToken, ValueTask<float[]?>>? embed,
        string text,
        CancellationToken cancellationToken)
    {
        if (embed is null)
        {
            return null;
        }

        return await embed(text, cancellationToken).ConfigureAwait(false);
    }

    private static IReadOnlySet<string> Tokenize(string text)
        => TextTokens.Split(text).ToHashSet(StringComparer.Ordinal);

    private static Dictionary<string, double> BuildIdf(IReadOnlyList<IndexedCapability> entries)
    {
        var documentFrequency = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            foreach (var token in entry.Tokens)
            {
                documentFrequency[token] = documentFrequency.GetValueOrDefault(token) + 1;
            }
        }

        var total = entries.Count;
        var idf = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (token, frequency) in documentFrequency)
        {
            idf[token] = Math.Log((total + 1d) / (frequency + 1d)) + 1d;
        }

        return idf;
    }

    private double TokenIdf(string token) => _inverseDocumentFrequency.GetValueOrDefault(token, 1d);
}
