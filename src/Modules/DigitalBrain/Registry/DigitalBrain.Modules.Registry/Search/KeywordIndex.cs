namespace DigitalBrain.Registry.Search;

// IDF-weighted token overlap: the precise half of hybrid search. Scores are the matched share of
// the query's weight, so 1 means every meaningful query token appears in the capability.
internal sealed class KeywordIndex
{
    private readonly IReadOnlyList<(CapabilityDocument Document, IReadOnlySet<string> Tokens)> _entries;
    private readonly Dictionary<string, double> _inverseDocumentFrequency;
    private readonly Dictionary<string, List<CapabilityDocument>> _aliases;

    public KeywordIndex(IReadOnlyList<CapabilityDocument> documents)
    {
        _entries = [.. documents.Select(static document => (document,
            (IReadOnlySet<string>)TextTokens.Split(string.Join(' ', document.Id, document.Name, document.Description)).ToHashSet(StringComparer.Ordinal)))];
        _inverseDocumentFrequency = _entries.SelectMany(static entry => entry.Tokens)
            .GroupBy(static token => token, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, group => Math.Log((_entries.Count + 1d) / (group.Count() + 1d)) + 1d, StringComparer.Ordinal);
        _aliases = new(StringComparer.OrdinalIgnoreCase);
        foreach (var document in documents.Where(static document => document.Kind is not CapabilityKind.Operation))
        {
            AddAlias(document.Id, document);
            if (document.Kind == CapabilityKind.App) { AddAlias(document.Id.Replace('.', ' '), document); }
        }
    }

    public static KeywordIndex Empty { get; } = new([]);

    public CapabilityDocument? Alias(string query, Func<CapabilityDocument, bool> visible)
        => _aliases.TryGetValue(query.Trim(), out var matches) ? matches.FirstOrDefault(visible) : null;

    public IEnumerable<(CapabilityDocument Document, double Score)> Score(string query, Func<CapabilityDocument, bool> visible)
    {
        var queryTokens = TextTokens.Split(query).Distinct(StringComparer.Ordinal).ToArray();
        var totalWeight = queryTokens.Sum(TokenWeight);
        if (totalWeight <= 0) { yield break; }
        foreach (var (document, tokens) in _entries)
        {
            if (!visible(document)) { continue; }
            var matched = queryTokens.Where(tokens.Contains).Sum(TokenWeight);
            if (matched > 0) { yield return (document, matched / totalWeight); }
        }
    }

    private void AddAlias(string key, CapabilityDocument document)
    {
        if (!_aliases.TryGetValue(key, out var list)) { _aliases[key] = list = []; }
        list.Add(document);
    }

    private double TokenWeight(string token) => _inverseDocumentFrequency.GetValueOrDefault(token, 1d);
}