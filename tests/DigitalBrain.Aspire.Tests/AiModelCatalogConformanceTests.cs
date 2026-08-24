using System.Reflection;
using DigitalBrain.AI;
using Xunit;

namespace DigitalBrain.Aspire.Tests;

// Catalogs are hand-written ordered lists because order is semantic: LLMModel.All
// is documented as "cloud models precede local ones" so the default-model fallback
// picks the first provider holding credentials. Assembly scanning would keep the
// lists honest but throw that ordering away, so the ordering stays and these tests
// take on the job scanning would have done.
//
// Reflection here is confined to the Contracts assembly, which carries no native
// dependencies — the hazard recorded on the old WhisperModel catalog applied only
// because that catalog sat in the implementation assembly beside Foundry Local.
public sealed class AiModelCatalogConformanceTests
{
    private static readonly Assembly Contracts = typeof(AiModel).Assembly;

    private sealed record ModelKind(
        string Name,
        Type Base,
        Type MarkerRoot,
        IReadOnlyList<AiModel> Catalog);

    // Later phases add transcription and image kinds here; every test below picks
    // them up with no further change.
    private static readonly ModelKind[] Kinds =
    [
        new("LLM", typeof(LLMModel), typeof(ILLM), LLMModel.All),
        new("Embedding", typeof(EmbeddingModel), typeof(IEmbedding), EmbeddingModel.All),
    ];

    [Fact]
    public void EveryModelDeclaredInContractsIsCatalogued()
    {
        var missing = new List<string>();

        foreach (var kind in Kinds)
        {
            var catalogued = kind.Catalog.Select(static model => model.GetType()).ToHashSet();
            var declared = Contracts.GetTypes()
                .Where(type => !type.IsAbstract && kind.Base.IsAssignableFrom(type));

            missing.AddRange(declared
                .Where(type => !catalogued.Contains(type))
                .Select(type => $"{type.FullName} is missing from {kind.Base.Name}.All"));
        }

        Assert.True(missing.Count == 0, string.Join(Environment.NewLine, missing));
    }

    [Fact]
    public void NoCatalogueRepeatsAWireId()
    {
        var duplicates = Kinds
            .SelectMany(kind => kind.Catalog
                .GroupBy(static model => model.Id, StringComparer.OrdinalIgnoreCase)
                .Where(static group => group.Count() > 1)
                .Select(group => $"{kind.Name}: '{group.Key}' is declared by "
                    + string.Join(", ", group.Select(static model => model.GetType().Name))))
            .ToList();

        Assert.True(duplicates.Count == 0, string.Join(Environment.NewLine, duplicates));
    }

    [Fact]
    public void EveryMarkerIsAnInterfaceOfItsOwnKind()
    {
        var wrong = new List<string>();

        foreach (var kind in Kinds)
        {
            foreach (var model in kind.Catalog)
            {
                if (!model.Marker.IsInterface)
                {
                    wrong.Add($"{model.GetType().Name}: marker {model.Marker.Name} is not an interface");
                    continue;
                }

                if (!kind.MarkerRoot.IsAssignableFrom(model.Marker))
                {
                    wrong.Add($"{model.GetType().Name}: marker {model.Marker.Name} does not implement {kind.MarkerRoot.Name}");
                }
            }
        }

        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    [Fact]
    public void NoMarkerIsClaimedByTwoModels()
    {
        // Markers key the DI registration, so a shared marker would silently make
        // one model unreachable rather than fail at registration.
        var duplicates = Kinds
            .SelectMany(static kind => kind.Catalog)
            .GroupBy(static model => model.Marker)
            .Where(static group => group.Count() > 1)
            .Select(static group => $"{group.Key.Name} is claimed by "
                + string.Join(", ", group.Select(static model => model.GetType().Name)))
            .ToList();

        Assert.True(duplicates.Count == 0, string.Join(Environment.NewLine, duplicates));
    }

    [Fact]
    public void EveryMarkerRootDescendsFromIAiMarker()
    {
        var wrong = Kinds
            .Where(static kind => !typeof(IAiMarker).IsAssignableFrom(kind.MarkerRoot))
            .Select(static kind => $"{kind.MarkerRoot.Name} does not implement {nameof(IAiMarker)}")
            .ToList();

        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    [Fact]
    public void EveryModelCarriesAWireIdAndADisplayName()
    {
        var blank = Kinds
            .SelectMany(static kind => kind.Catalog)
            .Where(static model =>
                string.IsNullOrWhiteSpace(model.Id) || string.IsNullOrWhiteSpace(model.DisplayName))
            .Select(static model => $"{model.GetType().Name} has a blank Id or DisplayName")
            .ToList();

        Assert.True(blank.Count == 0, string.Join(Environment.NewLine, blank));
    }

    [Theory]
    [InlineData(typeof(IPrefixedMarker), "PrefixedMarker")]
    [InlineData(typeof(Ideal), "Ideal")]
    [InlineData(typeof(Unprefixed), "Unprefixed")]
    public void DefaultDisplayNameDropsOnlyAnInterfacePrefix(Type marker, string expected)
    {
        // DisplayName is presentation, never a lookup key. This redesign exists in
        // part to delete a lookup that worked by chopping a leading 'I', so the one
        // surviving cosmetic use of that trick is pinned here — including the cases
        // it must NOT fire on: a name whose second character is lowercase, and one
        // with no interface prefix at all. Real models stay free to override
        // DisplayName outright.
        Assert.Equal(expected, new DisplayNameProbe(marker).DisplayName);
    }

    private interface IPrefixedMarker : IAiMarker;

    // 'I' followed by a lowercase letter is a word, not a prefix.
    private interface Ideal : IAiMarker;

#pragma warning disable CA1715 // Deliberately unprefixed: the case the strip must not fire on.
    private interface Unprefixed : IAiMarker;
#pragma warning restore CA1715

    private sealed class DisplayNameProbe(Type marker) : AiModel
    {
        public override string Id => "probe";

        public override AiProvider Provider => AiProvider.OpenAI;

        public override Type Marker { get; } = marker;
    }
}
