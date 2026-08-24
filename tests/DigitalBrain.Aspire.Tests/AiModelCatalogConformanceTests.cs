using System.Reflection;
using DigitalBrain.AI;
using OpenAI.Images;
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
        IReadOnlyList<AiModel> Catalog,
        Func<Type, AiModel?> FindByMarker,
        Func<string, AiModel?> FindByMarkerName);

    // Adding a kind here is all a new model family needs; every test below picks
    // it up with no further change.
    private static readonly ModelKind[] Kinds =
    [
        new("Image", typeof(ImageModel), typeof(IImageModel), ImageModel.All,
            ImageModel.FindByMarker, ImageModel.FindByMarkerName),
        new("LLM", typeof(LLMModel), typeof(ILLM), LLMModel.All,
            LLMModel.FindByMarker, LLMModel.FindByMarkerName),
        new("Embedding", typeof(EmbeddingModel), typeof(IEmbedding), EmbeddingModel.All,
            EmbeddingModel.FindByMarker, EmbeddingModel.FindByMarkerName),
        new("Transcription", typeof(TranscriptionModel), typeof(ITranscription), TranscriptionModel.All,
            TranscriptionModel.FindByMarker, TranscriptionModel.FindByMarkerName),
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
    public void EveryMarkerRoundTripsThroughItsCatalogue()
    {
        // Both lookups back a config key: FindByMarkerName resolves what
        // Default:Model, Default:Embedding, and Default:Transcription name.
        var broken = new List<string>();

        foreach (var kind in Kinds)
        {
            foreach (var model in kind.Catalog)
            {
                if (kind.FindByMarker(model.Marker) != model)
                {
                    broken.Add($"{kind.Name}: FindByMarker({model.Marker.Name}) did not return {model.GetType().Name}");
                }

                if (kind.FindByMarkerName(model.Marker.Name) != model)
                {
                    broken.Add($"{kind.Name}: FindByMarkerName(\"{model.Marker.Name}\") did not return {model.GetType().Name}");
                }
            }
        }

        Assert.True(broken.Count == 0, string.Join(Environment.NewLine, broken));
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

    [Fact]
    public void EveryEmbeddingModelDeclaresAPositiveVectorWidth()
    {
        // A stored collection is keyed to one width; a zero or negative one could
        // only ever be an unfilled placeholder.
        var invalid = EmbeddingModel.All
            .Where(static model => model.Dimensions <= 0)
            .Select(static model => $"{model.GetType().Name} declares Dimensions = {model.Dimensions}")
            .ToList();

        Assert.True(invalid.Count == 0, string.Join(Environment.NewLine, invalid));
    }

    [Fact]
    public void ImageModelsOnlyRequestAResponseFormatWhenTheyAcceptOne()
    {
        // Found in production: gpt-image-1 answers HTTP 400 unknown_parameter when
        // sent a response format. The request must carry the option only for models
        // that take it.
        foreach (var model in ImageModel.All)
        {
            var options = OpenAIImageGeneration.OptionsFor(model);

            // Branches rather than a ternary: GeneratedImageFormat is a struct with
            // an implicit conversion from string, so `cond ? Bytes : null` converts
            // the null through string and throws inside the SDK.
            if (model.AcceptsResponseFormat)
            {
                Assert.Equal(GeneratedImageFormat.Bytes, options.ResponseFormat);
            }
            else
            {
                Assert.Null(options.ResponseFormat);
            }
        }
    }

    [Fact]
    public void EveryTranscriptionModelCanReturnPlainText()
    {
        // The voice endpoint wants text and nothing else. A model that cannot
        // produce it has no way to serve that path, so it must not be catalogued
        // as though it could.
        var unusable = TranscriptionModel.All
            .Where(static model => !model.Formats.HasFlag(TranscriptionFormats.Text))
            .Select(static model => $"{model.GetType().Name} declares {model.Formats} and cannot return text")
            .ToList();

        Assert.True(unusable.Count == 0, string.Join(Environment.NewLine, unusable));
    }

    [Fact]
    public void LocalTranscriptionModelsArePreferredBestFirst()
    {
        // The Foundry service walks the local entries in catalogue order when the
        // configured model is absent from the machine. That ordering replaced
        // WhisperModel.Priority, so it is load-bearing rather than cosmetic.
        var local = TranscriptionModel.All
            .Where(static model => model.Provider is AiProvider.FoundryLocal)
            .Select(static model => model.Id)
            .ToList();

        Assert.Equal(["whisper-large-v3-turbo", "whisper-small", "whisper-tiny"], local);
    }

    // Only each type's NAME SHAPE matters here, never what the type is:
    //   ILLM   -> 'I' + uppercase, a real interface prefix
    //   Int32  -> 'I' + lowercase, a word that merely starts with I
    //   String -> no prefix at all
    // Borrowing existing types beats declaring deliberately misnamed ones, which
    // the repo's naming rules reject outright.
    [Theory]
    [InlineData(typeof(ILLM), "LLM")]
    [InlineData(typeof(int), "Int32")]
    [InlineData(typeof(string), "String")]
    public void DefaultDisplayNameDropsOnlyAnInterfacePrefix(Type markerNameSource, string expected)
    {
        // DisplayName is presentation, never a lookup key. This redesign exists in
        // part to delete a lookup that worked by chopping a leading 'I', so the one
        // surviving cosmetic use of that trick is pinned here — including the cases
        // it must NOT fire on. Real models stay free to override DisplayName.
        Assert.Equal(expected, new DisplayNameProbe(markerNameSource).DisplayName);
    }

    private sealed class DisplayNameProbe(Type marker) : AiModel
    {
        public override string Id => "probe";

        public override AiProvider Provider => AiProvider.OpenAI;

        public override Type Marker { get; } = marker;
    }
}
