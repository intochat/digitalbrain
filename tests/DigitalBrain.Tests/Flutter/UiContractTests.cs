using System.Text.Json;
using System.Xml.Linq;
using Brain.Contracts;
using Flutter.Contracts;
using Xunit;

namespace DigitalBrain.Tests.Flutter;

public sealed class UiContractTests
{
    [Fact]
    public void Contract_project_depends_only_on_brain_contracts()
    {
        var project = XDocument.Load(Path.Combine(
            RepositoryRoot(),
            "modules",
            "Flutter.Contracts",
            "Flutter.Contracts.csproj"));
        var references = project.Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value.Replace('\\', '/'))
            .ToArray();

        Assert.Equal(["../../kernel/Brain.Contracts/Brain.Contracts.csproj"], references);
        Assert.Empty(project.Descendants("PackageReference"));
    }

    [Fact]
    public void Window_neuron_exposes_the_transport_neutral_document()
    {
        var method = typeof(IWindowNeuron).GetMethod(nameof(IWindowNeuron.RenderAsync));

        Assert.NotNull(method);
        Assert.Equal(typeof(Task<WindowReply>), method.ReturnType);
        Assert.Equal(typeof(UiDocument), method.GetParameters().Single().ParameterType);
        Assert.Equal(
            "window.render.v1",
            method.GetCustomAttributes(typeof(NeuronContractAttribute), false)
                .Cast<NeuronContractAttribute>()
                .Single()
                .Contract);
    }

    [Fact]
    public void Canonical_v1_fixture_round_trips_without_wire_drift()
    {
        var fixture = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "workspace",
            "test",
            "fixtures",
            "ui_document_v1",
            "basic.json"));

        var document = UiDocument.Parse(fixture);
        var serialized = JsonSerializer.Serialize(document, JsonSerializerOptions.Web);

        using var expected = JsonDocument.Parse(fixture);
        using var actual = JsonDocument.Parse(serialized);
        Assert.True(JsonElement.DeepEquals(expected.RootElement, actual.RootElement));
    }

    [Theory]
    [InlineData("""{"version":1,"blocks":[{"kind":"unknown","text":"x"}]}""")]
    [InlineData("""{"version":1,"blocks":[{"kind":"text","text":"x","action":{"contract":"chat.post.v1","target":"owner|space|chat/x","inputJson":"{"}}]}""")]
    public void Invalid_v1_documents_are_rejected(string json)
    {
        var exception = Assert.Throws<BrainException>(() => UiDocument.Parse(json));
        Assert.Equal("input.invalid", exception.Code);
    }

    [Fact]
    public void Excessive_nesting_and_oversized_text_are_rejected()
    {
        var nested = """{"kind":"card","children":[]}""";
        for (var depth = 0; depth < 9; depth++)
            nested = $$"""{"kind":"card","children":[{{nested}}]}""";

        Assert.Throws<BrainException>(() => UiDocument.Parse($$"""{"version":1,"blocks":[{{nested}}]}"""));
        Assert.Throws<BrainException>(() => UiDocument.Parse(JsonSerializer.Serialize(new
        {
            version = 1,
            blocks = new[] { new { kind = "text", text = new string('x', 16_385) } }
        })));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Packages.props")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
