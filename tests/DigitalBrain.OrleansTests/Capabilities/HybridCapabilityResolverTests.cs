using DigitalBrain.Integrations.Google;
using DigitalBrain.Integrations.Google.Contracts;
using DigitalBrain.Integrations.Salesforce;
using DigitalBrain.Integrations.Salesforce.Contracts;
using DigitalBrain.Kernel.Capabilities;
using Microsoft.Extensions.AI;

namespace DigitalBrain.OrleansTests.Capabilities;

public sealed class HybridCapabilityResolverTests
{
    [Fact]
    public async Task ResolveAsync_selects_salesforce_read_from_semantic_similarity()
    {
        var resolver = Resolver(new Dictionary<string, float[]>
        {
            ["Find Acme in our CRM"] = [1, 0],
            [Document(SalesforceCapabilityIds.RecordRead)] = [1, 0],
            [Document(GoogleCapabilityIds.GmailMessageRead)] = [0, 1]
        });

        var result = await resolver.ResolveAsync(Request("Find Acme in our CRM", connections: ["salesforce"]));

        Assert.Equal(CapabilityResolutionKind.Match, result.Receipt.Kind);
        Assert.Equal(SalesforceCapabilityIds.RecordRead, result.Receipt.CapabilityId);
    }

    [Fact]
    public async Task ResolveAsync_falls_back_to_lexical_scoring_for_zero_vectors()
    {
        var result = await ResolverWithZeroVectors().ResolveAsync(Request("read gmail messages", connections: ["google"]));

        Assert.Equal(GoogleCapabilityIds.GmailMessageRead, result.Receipt.CapabilityId);
    }

    [Fact]
    public async Task ResolveAsync_returns_ambiguous_when_top_scores_are_too_close()
    {
        var result = await ResolverWithEqualVectors().ResolveAsync(Request("show customer records", connections: ["google", "salesforce"]));

        Assert.Equal(CapabilityResolutionKind.Ambiguous, result.Receipt.Kind);
        Assert.True(result.Receipt.CandidateIds.Length >= 2);
    }

    [Fact]
    public async Task ResolveAsync_filters_missing_grants_before_scoring()
    {
        var result = await ResolverWithExactVectors().ResolveAsync(Request("send an email", connections: ["google"]));

        Assert.DoesNotContain(GoogleCapabilityIds.GmailSendPropose, result.Receipt.CandidateIds);
        Assert.NotEqual(GoogleCapabilityIds.GmailSendPropose, result.Receipt.CapabilityId);
    }

    private static BuiltInCapabilityCatalog Catalog() =>
        new([new GoogleCapabilityDescriptorSource(), new SalesforceCapabilityDescriptorSource()]);

    private static HybridCapabilityResolver Resolver(IReadOnlyDictionary<string, float[]> vectors) =>
        new(Catalog(), new FakeEmbeddingGenerator(vectors));

    private static HybridCapabilityResolver ResolverWithZeroVectors() =>
        Resolver(new Dictionary<string, float[]>(StringComparer.Ordinal));

    private static HybridCapabilityResolver ResolverWithEqualVectors() =>
        Resolver(new Dictionary<string, float[]>
        {
            ["show customer records"] = [1, 1],
            [Document(SalesforceCapabilityIds.RecordRead)] = [1, 1],
            [Document(GoogleCapabilityIds.GmailMailboxRead)] = [1, 1]
        });

    private static HybridCapabilityResolver ResolverWithExactVectors() =>
        Resolver(new Dictionary<string, float[]>
        {
            ["send an email"] = [1, 0],
            [Document(GoogleCapabilityIds.GmailSendPropose)] = [1, 0]
        });

    private static CapabilitySearchRequest Request(string prompt, IEnumerable<string>? grants = null, IEnumerable<string>? connections = null) =>
        new(
            prompt,
            (grants ?? []).ToHashSet(StringComparer.Ordinal),
            (connections ?? []).ToHashSet(StringComparer.Ordinal));

    private static string Document(string capabilityId) =>
        HybridCapabilityResolver.SearchDocument(Catalog().Snapshot().First(descriptor => descriptor.Id == capabilityId));

    private sealed class FakeEmbeddingGenerator(IReadOnlyDictionary<string, float[]> vectors) : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var embeddings = values
                .Select(value => new Embedding<float>(vectors.TryGetValue(value, out var vector) ? vector : [0, 0]))
                .ToList();
            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(embeddings));
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
