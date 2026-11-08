using System.Xml.Linq;

namespace DigitalBrain.Tests.Unit;

// The Orleans-free legacy types copied from os IntoChat.OS (03-03); 03-10 deletes the os copies.
public sealed class LegacyPureTypeFacts
{
    [Theory]
    [InlineData("DigitalBrain.Contracts.NeuronContractAttribute")]
    [InlineData("DigitalBrain.Contracts.NeuronAliasAttribute")]
    [InlineData("DigitalBrain.Contracts.FieldAttribute")]
    [InlineData("DigitalBrain.Contracts.DefaultNeuronTypeAttribute")]
    [InlineData("DigitalBrain.Access.AccessAttribute")]
    [InlineData("DigitalBrain.Access.PublicAttribute")]
    [InlineData("DigitalBrain.Access.SharedAttribute")]
    [InlineData("DigitalBrain.Access.PerAccountAttribute")]
    [InlineData("DigitalBrain.Access.InternalAttribute")]
    [InlineData("DigitalBrain.State.ExportableStateAttribute")]
    [InlineData("DigitalBrain.State.StoredRecord")]
    [InlineData("DigitalBrain.State.WriteResult")]
    [InlineData("DigitalBrain.State.IStateCodec")]
    public void Copied_type_exists(string fullName)
    {
        var type = typeof(Signal).Assembly.GetType(fullName);
        Assert.NotNull(type);
        Assert.True(type.IsPublic, fullName);
    }

    [Fact]
    public void Alias_helper_is_not_copied()
    {
        Assert.Null(typeof(DigitalBrain.Contracts.NeuronAliasAttribute).GetMethod("Of"));
    }

    // 03-04 relaxation: one ProjectReference to DigitalBrain.Analyzers, analyzer-only; 0.18.0: plus DigitalBrain.UI (a plain reference,
    // which references no project), because a state neuron stores its View. Anything else fails.
    [Fact]
    public void Core_references_only_the_analyzer_as_analyzer_and_the_view_model()
    {
        Assert.True(Allowed(CoreReferences(LoadCore())), LoadCore().ToString());
    }

    [Theory]
    [InlineData("<ProjectReference Include=\"../DigitalBrain.UI/DigitalBrain.UI.csproj\" />")]
    [InlineData("<ProjectReference Include=\"../DigitalBrain.Analyzers/DigitalBrain.Analyzers.csproj\" ReferenceOutputAssembly=\"false\" OutputItemType=\"Analyzer\" /><ProjectReference Include=\"../Other/Other.csproj\" />")]
    [InlineData("<ProjectReference Include=\"../DigitalBrain.Analyzers/DigitalBrain.Analyzers.csproj\" />")]
    [InlineData("<ProjectReference Include=\"../DigitalBrain.Analyzers/DigitalBrain.Analyzers.csproj\" ReferenceOutputAssembly=\"true\" OutputItemType=\"Analyzer\" />")]
    [InlineData("<ProjectReference Include=\"../Other/Other.csproj\" ReferenceOutputAssembly=\"false\" OutputItemType=\"Analyzer\" />")]
    public void Any_other_reference_is_rejected(string reference)
    {
        var csproj = XDocument.Parse($"<Project><ItemGroup>{reference}</ItemGroup></Project>");
        Assert.False(Allowed(CoreReferences(csproj)));
    }

    // Exactly the analyzer-only reference and the view model reference.
    private static bool Allowed(List<XElement> references)
        => references.Count == 2 && references.Count(IsAnalyzerOnly) == 1 && references.Count(IsViewModel) == 1;

    private static bool IsViewModel(XElement reference)
        => Path.GetFileName((reference.Attribute("Include")?.Value ?? string.Empty).Replace('\\', '/')) == "DigitalBrain.UI.csproj"
            && reference.Attribute("OutputItemType") is null;

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
