using System.Text.Json;
using DigitalBrain.Apps;
using Xunit;

namespace DigitalBrain.Modules.Apps.Tests;

public sealed class AppDocumentFacts
{
    internal static AppAuthoringDocument Document() => new(1, "A small app.",
        [new(Guid.NewGuid().ToString("N"), "Research", "Read observed pages.", [], ["11111111111111111111111111111111"])],
        [new("11111111111111111111111111111111", "Research saves", "Invoking research saves the result.", false)]);

    [Fact]
    public void DocumentsRoundTripWithStableIdentitiesAndDescriptions()
    {
        var document = Document();
        var restored = JsonSerializer.Deserialize<AppAuthoringDocument>(AppDocumentCodec.Encode(document), AppDocumentCodec.Json)!;
        Assert.Equal(document.Behaviors[0].Id, restored.Behaviors[0].Id);
        var spec = AppDocumentCodec.ExportSpec(restored);
        Assert.Contains("Read observed pages.", spec);
        Assert.Equal(1, spec.Split("## Scenario: Research saves").Length - 1);
    }

    [Fact]
    public void AmbiguousNamesAndDanglingLinksCannotBecomeStructuredDocuments()
    {
        var d = Document();
        Assert.Throws<ArgumentException>(() => AppDocumentCodec.Validate(d with { Scenarios = [d.Scenarios[0], d.Scenarios[0] with { Id = Guid.NewGuid().ToString("N") }] }));
        Assert.Throws<ArgumentException>(() => AppDocumentCodec.Validate(d with { Scenarios = [] }));
        Assert.Throws<ArgumentException>(() => AppDocumentCodec.Validate(d with { Preamble = "## Scenario: Injected" }));
    }

    [Fact]
    public void ScenarioNamesCannotContainTheFailureProtocolSeparator()
    {
        var document = Document();
        var invalid = document with { Scenarios = [document.Scenarios[0] with { Name = "Save\tresult" }] };
        Assert.Throws<ArgumentException>(() => AppDocumentCodec.Validate(invalid));
    }

    [Fact]
    public void SourceReferencesArePackageKeysAndMustResolveWhenBuilt()
    {
        var d = Document();
        d = d with { Behaviors = [d.Behaviors[0] with { SourcePaths = ["behaviors/research.cs"] }] };
        AppDocumentCodec.Validate(d);
        Assert.Throws<ArgumentException>(() => AppDocumentCodec.Validate(d, new Dictionary<string, string>(), true));
        AppDocumentCodec.Validate(d, new Dictionary<string, string> { ["behaviors/research.cs"] = "source" }, true);
        Assert.Throws<ArgumentException>(() => AppDocumentCodec.Validate(d with { Behaviors = [d.Behaviors[0] with { SourcePaths = ["../secret.cs"] }] }));
    }

    [Fact]
    public void UnknownMetadataPreservesTheOriginalDocument()
    {
        const string original = "arbitrary\r\n## Scenario: broken\r\ntext";
        var read = AppDocumentCodec.Read(original, "{\"version\":99}");
        Assert.Equal(original, read.OriginalSpec);
        Assert.False(read.CanEdit);
        Assert.NotNull(read.Error);
        Assert.Equal(original, AppDocumentCodec.Read(original, null).OriginalSpec);
    }

    [Fact]
    public void AFinalBlankLineDoesNotChangeTheAuthoringDocument()
    {
        var document = Document();
        var content = new PackageContent(new("App", "", [], []), "", new()
        {
            [AppDocumentCodec.Path] = AppDocumentCodec.Encode(document),
            [PackageContent.SpecPath] = AppDocumentCodec.ExportSpec(document).TrimEnd('\n') + "\n",
        });
        Assert.True(AppDocumentCodec.Read(content).CanEdit);
    }

    [Fact]
    public void MetadataCannotClaimDifferentProseThanTheVerifiedSpec()
    {
        var d = Document();
        var content = new PackageContent(new("App", "", [], []), "", new Dictionary<string, string> { [AppDocumentCodec.Path] = AppDocumentCodec.Encode(d), [PackageContent.SpecPath] = "Different program" });
        Assert.False(AppDocumentCodec.Read(content).CanEdit);
        Assert.Null(AppDocumentCodec.Read(content).Document);
    }
}
