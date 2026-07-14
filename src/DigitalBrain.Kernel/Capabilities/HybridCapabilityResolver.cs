using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
namespace DigitalBrain.Kernel.Capabilities;

internal sealed record RankedCapability(CapabilityDescriptor Descriptor, double Exact, double Lexical, double Vector)
{
    public double Score { get; init; }
}

internal sealed class HybridCapabilityResolver(
    ICapabilityCatalog catalog,
    IEmbeddingGenerator<string, Embedding<float>> embedder) : ICapabilityResolver
{
    internal const double MatchThreshold = 0.68;
    internal const double AmbiguityMargin = 0.06;
    internal const int MaximumPromptLength = 4096;
    private static readonly Regex TokenPattern = new("[a-z0-9]+", RegexOptions.Compiled);

    public async Task<CapabilityResolution> ResolveAsync(
        CapabilitySearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Prompt);
        if (request.Prompt.Length > MaximumPromptLength || request.MaximumMatches is < 1 or > 5)
            throw new ArgumentException("Capability search bounds are invalid.", nameof(request));

        var candidates = catalog.Snapshot()
            .Where(x => x.Available)
            .Where(x => x.RequiredGrants.All(request.Grants.Contains))
            .Where(x => x.RequiredConnections.All(request.Connections.Contains))
            .OrderBy(x => x.Id, StringComparer.Ordinal)
            .ToArray();
        if (candidates.Length == 0) return Missing();

        var query = Normalize(request.Prompt);
        var documents = candidates.Select(SearchDocument).ToArray();
        GeneratedEmbeddings<Embedding<float>>? generated;
        try
        {
            generated = await embedder.GenerateAsync([request.Prompt, .. documents], cancellationToken: cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            generated = null;
        }
        var vectorEnabled = generated is not null && generated[0].Vector.Span.IndexOfAnyExcept(0f) >= 0;
        var ranked = candidates.Select((descriptor, index) => new RankedCapability(
                descriptor,
                Exact(query, descriptor),
                Lexical(query, documents[index]),
                vectorEnabled ? Cosine(generated![0].Vector.Span, generated[index + 1].Vector.Span) : 0))
            .Select(x => x with
            {
                Score = vectorEnabled
                    ? Math.Max(0.65 * x.Exact + 0.35 * x.Lexical, 0.70 * x.Vector + 0.30 * x.Lexical)
                    : 0.65 * x.Exact + 0.35 * x.Lexical
            })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Descriptor.Id, StringComparer.Ordinal)
            .ToArray();

        var reported = ranked.Take(request.MaximumMatches).ToArray();
        var first = ranked[0];
        if (first.Score < MatchThreshold) return Missing(reported);
        if (ranked.Length > 1 && first.Score - ranked[1].Score < AmbiguityMargin)
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
