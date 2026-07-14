using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
namespace DigitalBrain.Kernel.Capabilities;

internal sealed record RankedCapability(
    CapabilityDescriptor Descriptor,
    double Exact,
    double Lexical,
    double LexicalFallback,
    int LexicalMatches,
    double Vector)
{
    public double Score { get; init; }
}

internal sealed class HybridCapabilityResolver(
    ICapabilityCatalog catalog,
    IEmbeddingGenerator<string, Embedding<float>> embedder) : ICapabilityResolver
{
    internal const double MatchThreshold = 0.68;
    internal const double LexicalFallbackThreshold = 0.75;
    internal const double AmbiguityMargin = 0.06;
    internal const int MaximumPromptLength = 4096;
    internal const int MinimumLexicalFallbackMatches = 3;
    private static readonly Regex TokenPattern = new("[a-z0-9]+", RegexOptions.Compiled);

    public async Task<CapabilityResolution> ResolveAsync(
        CapabilitySearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Prompt);
        if (request.Prompt.Length > MaximumPromptLength || request.MaximumMatches is < 1 or > 5)
            throw new ArgumentException("Capability search bounds are invalid.", nameof(request));
        cancellationToken.ThrowIfCancellationRequested();

        var candidates = catalog.Snapshot()
            .Where(x => x.Available)
            .Where(x => x.RequiredGrants.All(request.Grants.Contains))
            .Where(x => x.RequiredConnections.All(request.Connections.Contains))
            .OrderBy(x => x.Id, StringComparer.Ordinal)
            .ToArray();
        if (candidates.Length == 0) return Missing();

        var query = Normalize(request.Prompt);
        var documents = candidates.Select(SearchDocument).ToArray();
        var ranked = candidates.Select((descriptor, index) =>
            {
                var lexicalFallback = LexicalFallback(query, descriptor, documents[index]);
                return new RankedCapability(
                    descriptor,
                    Exact(query, descriptor),
                    Lexical(query, documents[index]),
                    lexicalFallback.Score,
                    lexicalFallback.Matches,
                    0);
            })
            .ToArray();

        var exactRanked = ranked
            .OrderByDescending(x => x.Exact)
            .ThenByDescending(x => x.LexicalFallback)
            .ThenBy(x => x.Descriptor.Id, StringComparer.Ordinal)
            .Select(x => x with { Score = x.Exact })
            .ToArray();
        if (exactRanked[0].Exact == 1)
        {
            var reportedExact = exactRanked.Take(request.MaximumMatches).ToArray();
            if (exactRanked.Length > 1 && exactRanked[1].Exact == 1)
                return Ambiguous(reportedExact);
            return Match(exactRanked[0], reportedExact);
        }

        GeneratedEmbeddings<Embedding<float>>? generated;
        try
        {
            generated = await embedder.GenerateAsync([request.Prompt, .. documents], cancellationToken: cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            generated = null;
        }
        var vectorEnabled = HasUsableVectors(generated, documents.Length + 1);

        if (!vectorEnabled)
        {
            var lexicalRanked = ranked
                .OrderByDescending(x => x.LexicalFallback)
                .ThenByDescending(x => x.LexicalMatches)
                .ThenBy(x => x.Descriptor.Id, StringComparer.Ordinal)
                .Select(x => x with { Score = x.LexicalFallback })
                .ToArray();
            var reportedLexical = lexicalRanked.Take(request.MaximumMatches).ToArray();
            if (!IsStrongLexicalMatch(lexicalRanked[0])) return Missing(reportedLexical);
            if (lexicalRanked.Length > 1
                && IsStrongLexicalMatch(lexicalRanked[1])
                && lexicalRanked[0].Score - lexicalRanked[1].Score < AmbiguityMargin)
                return Ambiguous(reportedLexical);
            return Match(lexicalRanked[0], reportedLexical);
        }

        var semanticRanked = ranked
            .Select((x, index) => x with
            {
                Vector = Cosine(generated![0].Vector.Span, generated[index + 1].Vector.Span)
            })
            .Select(x => x with
            {
                Score = Math.Max(0.65 * x.Exact + 0.35 * x.Lexical, 0.70 * x.Vector + 0.30 * x.Lexical)
            })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Descriptor.Id, StringComparer.Ordinal)
            .ToArray();

        var reported = semanticRanked.Take(request.MaximumMatches).ToArray();
        var first = semanticRanked[0];
        if (first.Score < MatchThreshold) return Missing(reported);
        if (semanticRanked.Length > 1 && first.Score - semanticRanked[1].Score < AmbiguityMargin)
            return Ambiguous(reported);
        return Match(first, reported);
    }

    internal static string SearchDocument(CapabilityDescriptor descriptor) =>
        string.Join(' ', [descriptor.Id, descriptor.Name, descriptor.Description, .. descriptor.Examples]);

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();

    private static double Exact(string query, CapabilityDescriptor descriptor)
    {
        var normalizedName = Normalize(descriptor.Name);
        if (query == Normalize(descriptor.Id)
            || query == normalizedName
            || descriptor.Examples.Any(example => query == Normalize(example)))
            return 1;
        return query.Contains(normalizedName, StringComparison.Ordinal) ? 0.8 : 0;
    }

    private static double Lexical(string query, string document)
    {
        var left = Tokenize(query);
        var right = Tokenize(document);
        if (left.Count == 0 || right.Count == 0) return 0;
        var intersection = left.Intersect(right).Count();
        var union = left.Count + right.Count - intersection;
        return union == 0 ? 0 : (double)intersection / union;
    }

    private static (double Score, int Matches) LexicalFallback(
        string query,
        CapabilityDescriptor descriptor,
        string document)
    {
        var requested = Tokenize(query);
        if (requested.Count == 0) return (0, 0);
        var nameMatches = requested.Intersect(Tokenize(descriptor.Name)).Count();
        var documentMatches = requested.Intersect(Tokenize(document)).Count();
        var nameCoverage = (double)nameMatches / requested.Count;
        var documentCoverage = (double)documentMatches / requested.Count;
        return (0.8 * nameCoverage + 0.2 * documentCoverage, nameMatches);
    }

    private static bool IsStrongLexicalMatch(RankedCapability ranked) =>
        ranked.LexicalMatches >= MinimumLexicalFallbackMatches
        && ranked.LexicalFallback >= LexicalFallbackThreshold;

    private static bool HasUsableVectors(GeneratedEmbeddings<Embedding<float>>? generated, int expectedCount)
    {
        if (generated is null || generated.Count != expectedCount || generated[0].Vector.Length == 0) return false;
        var dimensions = generated[0].Vector.Length;
        return generated[0].Vector.Span.IndexOfAnyExcept(0f) >= 0
            && generated.All(embedding => embedding.Vector.Length == dimensions);
    }

    private static HashSet<string> Tokenize(string value) =>
        TokenPattern.Matches(value.ToLowerInvariant()).Select(match => Stem(match.Value)).ToHashSet(StringComparer.Ordinal);

    private static string Stem(string token) =>
        token.Length > 3 && token.EndsWith('s') ? token[..^1] : token;

    private static double Cosine(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        double dot = 0;
        double leftNorm = 0;
        double rightNorm = 0;
        for (var index = 0; index < left.Length; index++)
        {
            dot += left[index] * right[index];
            leftNorm += left[index] * left[index];
            rightNorm += right[index] * right[index];
        }
        if (leftNorm <= 0 || rightNorm <= 0) return 0;
        return Math.Clamp(dot / (Math.Sqrt(leftNorm) * Math.Sqrt(rightNorm)), 0, 1);
    }

    private static CapabilityResolution Missing() =>
        new(new CapabilityResolutionReceipt(CapabilityResolutionKind.Missing, null, null, [], 0), null, []);

    private static CapabilityResolution Missing(RankedCapability[] ranked) =>
        new(
            new CapabilityResolutionReceipt(
                CapabilityResolutionKind.Missing,
                ranked[0].Descriptor.Id,
                ranked[0].Descriptor.Name,
                ranked.Select(x => x.Descriptor.Id).ToArray(),
                ranked[0].Score),
            null,
            ranked.Select(x => x.Descriptor).ToArray());

    private static CapabilityResolution Ambiguous(RankedCapability[] ranked) =>
        new(
            new CapabilityResolutionReceipt(
                CapabilityResolutionKind.Ambiguous,
                null,
                null,
                ranked.Select(x => x.Descriptor.Id).ToArray(),
                ranked[0].Score),
            null,
            ranked.Select(x => x.Descriptor).ToArray());

    private static CapabilityResolution Match(RankedCapability first, RankedCapability[] ranked) =>
        new(
            new CapabilityResolutionReceipt(
                CapabilityResolutionKind.Match,
                first.Descriptor.Id,
                first.Descriptor.Name,
                ranked.Select(x => x.Descriptor.Id).ToArray(),
                first.Score),
            first.Descriptor,
            ranked.Select(x => x.Descriptor).ToArray());
}
