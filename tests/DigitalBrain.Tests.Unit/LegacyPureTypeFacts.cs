using System.Xml.Linq;

namespace DigitalBrain.Tests.Unit;

// The Orleans-free legacy types copied from os IntoChat.OS (03-03); 03-10 deletes the os copies.
public sealed class LegacyPureTypeFacts
{
    [Fact]
    public void Alias_helper_is_not_copied()
    {
        Assert.Null(typeof(DigitalBrain.Contracts.NeuronAliasAttribute).GetMethod("Of"));
    }

    // R10: DigitalBrain.csproj references DigitalBrain.UI and DigitalBrain.Analyzers (analyzer-only).
    [Fact]
    public void Core_references_only_the_analyzer_project_as_analyzer()
    {
        var references = CoreReferences(LoadCore());
        Assert.Equal(2, references.Count);
        Assert.Contains(references, r => IsAnalyzerOnly(r));
        Assert.Contains(references, r => Path.GetFileName((r.Attribute("Include")?.Value ?? string.Empty).Replace('\\', '/')) == "DigitalBrain.UI.csproj");
    }

    [Theory]
    [InlineData("<ProjectReference Include=\"../DigitalBrain.Analyzers/DigitalBrain.Analyzers.csproj\" />")]
    [InlineData("<ProjectReference Include=\"../DigitalBrain.Analyzers/DigitalBrain.Analyzers.csproj\" ReferenceOutputAssembly=\"true\" OutputItemType=\"Analyzer\" />")]
    [InlineData("<ProjectReference Include=\"../Other/Other.csproj\" ReferenceOutputAssembly=\"false\" OutputItemType=\"Analyzer\" />")]
    public void Any_other_reference_is_rejected(string reference)
    {
        var csproj = XDocument.Parse($"<Project><ItemGroup>{reference}</ItemGroup></Project>");
        Assert.False(CoreReferences(csproj).Any(IsAnalyzerOnly) && CoreReferences(csproj).Count == 2);
    }

    private static List<XElement> CoreReferences(XDocument csproj) => csproj.Descendants("ProjectReference").ToList();

    private static bool IsAnalyzerOnly(XElement reference)
        => Path.GetFileName((reference.Attribute("Include")?.Value ?? string.Empty).Replace('\\', '/')) == "DigitalBrain.Analyzers.csproj"
            && string.Equals(reference.Attribute("ReferenceOutputAssembly")?.Value, "false", StringComparison.OrdinalIgnoreCase)
            && string.Equals(reference.Attribute("OutputItemType")?.Value, "Analyzer", StringComparison.Ordinal);

    private static XDocument LoadCore()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DigitalBrain.slnx"))) { dir = dir.Parent; }
        Assert.NotNull(dir);
        return XDocument.Load(Path.Combine(dir.FullName, "src", "DigitalBrain", "DigitalBrain.csproj"));
    }
}
