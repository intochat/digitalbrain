using DigitalBrain.Core;

namespace DigitalBrain.Tests;

public sealed class ApplicationConfigurationFacts
{
    private sealed record Application(IReadOnlyList<ModuleDefinition> Modules) : IApplicationConfiguration;

    [Fact]
    public void SnapshotPreservesExplicitSettingsAndDoesNotBorrowDefaults()
    {
        var selected = new ModuleDefinition(typeof(CompositionFacts.ExampleModule),
            new Dictionary<string, string?> { ["Feature:Value"] = "chosen" });
        var text = ApplicationConfigurationTransport.Write(new Application([selected]));
        var result = ApplicationConfigurationTransport.Read(text,
            [new(typeof(CompositionFacts.ExampleModule), new Dictionary<string, string?> { ["Feature:Value"] = "ambient" })]);
        Assert.Equal("chosen", Assert.Single(result).Configuration["Feature:Value"]);
    }

    [Fact]
    public void MissingAndUnknownSlotsFailBeforeHosting()
    {
        var selected = new ModuleDefinition(typeof(CompositionFacts.ExampleModule));
        var text = ApplicationConfigurationTransport.Write(new Application([selected]));
        Assert.Throws<ArgumentException>(() => ApplicationConfigurationTransport.Read(text, []));
        Assert.Throws<ArgumentException>(() => ApplicationConfigurationTransport.Read(
            ApplicationConfigurationTransport.Write(new Application([])), [selected]));
    }

    [Theory]
    [InlineData("Orleans:ClusterId")]
    [InlineData("ConnectionStrings:storage")]
    [InlineData("DigitalBrain:Testing:Application")]
    [InlineData("DigitalBrain:Modules:0")]
    public void HarnessKeysCannotBeTransported(string key)
        => Assert.Throws<ArgumentException>(() => ApplicationConfigurationTransport.Write(
            new Application([new(typeof(CompositionFacts.ExampleModule), new Dictionary<string, string?> { [key] = "override" })])));

    [Fact]
    public void UnsupportedVersionIsRejected()
        => Assert.Throws<ArgumentException>(() => ApplicationConfigurationTransport.Read(
            "{\"Version\":99,\"Modules\":[]}", []));

    [Fact]
    public void OversizedSettingsAreRejected()
        => Assert.Throws<ArgumentException>(() => ApplicationConfigurationTransport.Write(
            new Application([new(typeof(CompositionFacts.ExampleModule),
                new Dictionary<string, string?> { ["Data"] = new string('x', 9000) })])));
}
