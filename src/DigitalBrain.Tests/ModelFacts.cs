using System.Reflection;
using System.Xml.Linq;
using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnitV3;
using Assembly = System.Reflection.Assembly;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace DigitalBrain.Tests;

public class ModelFacts
{
    private static readonly Assembly Model = typeof(Signal).Assembly;

    private static readonly Architecture Architecture =
        LoadArchitecture();

    private static Architecture LoadArchitecture()
    {
        var model = new ArchLoader().LoadAssembly(Model).Build();
        // OnlyDependOn checks loaded types; include referenced targets so external edges count.
        return new Architecture(model.Assemblies, model.Namespaces,
            model.Types.Concat(model.ReferencedTypes), model.GenericParameters, model.ReferencedTypes);
    }

    [Fact]
    public void The_model_depends_on_nothing_but_the_runtime()
    {
        Types().That().ResideInAssembly(Model).Should()
            .OnlyDependOnTypesThat().ResideInNamespaceMatching("^(System|DigitalBrain)($|\\.)")
            .Because("the model defines meaning on any runtime; Orleans and every other host stay in the Kernel ring")
            .Check(Architecture);
    }

    [Fact]
    public void The_model_assembly_references_only_the_runtime()
    {
        // ArchUnitNET sees type dependencies; this also checks emitted assembly links.
        Assert.All(Model.GetReferencedAssemblies(),
            reference => Assert.StartsWith("System", reference.Name));
    }

    [Fact]
    public void The_model_project_declares_no_dependencies()
    {
        // Unused build references may disappear from the compiled assembly altogether.
        var project = XDocument.Load(typeof(ModelFacts).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "ModelProject").Value!);
        Assert.DoesNotContain(project.Descendants(), element => element.Name.LocalName is
            "PackageReference" or "ProjectReference" or "FrameworkReference");
    }

    [Fact]
    public void A_default_neuron_id_prints_without_throwing()
    {
        Assert.Null(default(NeuronId).ToString());
        Assert.Equal("button-1", new NeuronId("button-1").ToString());
    }

    [Fact]
    public void Neuron_ids_are_values()
    {
        Assert.Equal(new NeuronId("a"), new NeuronId("a"));
        Assert.NotEqual(new NeuronId("a"), new NeuronId("b"));
    }

    [Fact]
    public void Signals_are_values_including_their_publisher()
    {
        Assert.Equal(new Clicked { Publisher = new("b1") }, new Clicked { Publisher = new("b1") });
        Assert.NotEqual(new Clicked { Publisher = new("b1") }, new Clicked { Publisher = new("b2") });
        Assert.Equal(default, new Clicked().Publisher);
    }

    private sealed record Clicked : Signal;
}
