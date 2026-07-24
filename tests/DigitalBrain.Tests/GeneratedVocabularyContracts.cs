using System.Collections.Immutable;
using System.Globalization;
using DigitalBrain.SourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class GeneratedVocabularyContracts
{
    private const string Framework = """
        namespace Orleans
        {
            [System.AttributeUsage(System.AttributeTargets.Interface)]
            public sealed class AliasAttribute(string value) : System.Attribute;
        }

        namespace Reqnroll
        {
            public sealed class BindingAttribute : System.Attribute;
        }

        namespace DigitalBrain.Abstractions
        {
            public interface INeuron;
            public abstract record Synapse;
        }

        namespace DigitalBrain.Client
        {
            public interface IDigitalBrain;
        }

        namespace DigitalBrain.Testing
        {
            public sealed class TestJournal;
            public sealed class TestNeuron<TNeuron>
                where TNeuron : class, DigitalBrain.Abstractions.INeuron;

            public sealed class TestOwner
            {
                public TestNeuron<TNeuron> Neuron<TNeuron>(string name)
                    where TNeuron : class, DigitalBrain.Abstractions.INeuron
                    => throw new System.NotImplementedException();
            }
        }
        """;

    [Fact]
    public void QualifiedAndUniqueShortNamesMapToCompiledDelegates()
    {
        var generated = Generate("""
            namespace Alpha
            {
                public partial interface IReceiver :
                    DigitalBrain.Abstractions.INeuron;

                public sealed record Ping(string Value) :
                    DigitalBrain.Abstractions.Synapse;
            }
            """);

        var vocabulary = generated.Source;

        Assert.Contains(
            "[\"Alpha.IReceiver\"] =",
            vocabulary,
            StringComparison.Ordinal);
        Assert.Contains(
            "[\"IReceiver\"] =",
            vocabulary,
            StringComparison.Ordinal);
        Assert.Contains(
            "owner.Neuron<global::Alpha.IReceiver>(name)",
            vocabulary,
            StringComparison.Ordinal);
        Assert.Contains(
            "neuron.Id,",
            vocabulary,
            StringComparison.Ordinal);
        Assert.Contains(
            "[\"Alpha.Ping\"] =",
            vocabulary,
            StringComparison.Ordinal);
        Assert.Contains(
            "[\"Ping\"] =",
            vocabulary,
            StringComparison.Ordinal);
        Assert.Contains(
            "new global::Alpha.Ping(",
            vocabulary,
            StringComparison.Ordinal);
        Assert.Contains(
            "journal.NextAsync<global::Alpha.Ping>(",
            vocabulary,
            StringComparison.Ordinal);
        Assert.Contains(
            "new TestSynapseObservation(",
            vocabulary,
            StringComparison.Ordinal);
        Assert.Contains(
            "observed.CorrelationId.Value.ToString(\"D\")",
            vocabulary,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AmbiguousShortNamesProduceSortedDiagnosticAndNoShortEntry()
    {
        var generated = Generate("""
            namespace Alpha
            {
                public sealed record Signal :
                    DigitalBrain.Abstractions.Synapse;
            }

            namespace Beta
            {
                public sealed record Signal :
                    DigitalBrain.Abstractions.Synapse;
            }
            """);

        var diagnostic = Assert.Single(
            generated.Diagnostics,
            candidate => candidate.Id == "DBGEN007");

        Assert.Contains(
            "Alpha.Signal, Beta.Signal",
            diagnostic.GetMessage(CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "[\"Signal\"] =",
            generated.Source,
            StringComparison.Ordinal);
        Assert.Contains(
            "[\"Alpha.Signal\"] =",
            generated.Source,
            StringComparison.Ordinal);
        Assert.Contains(
            "[\"Beta.Signal\"] =",
            generated.Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void FactoriesContainNoRuntimeReflectionOrOrleansAddressing()
    {
        var generated = Generate("""
            namespace Alpha
            {
                public partial interface IReceiver :
                    DigitalBrain.Abstractions.INeuron;

                public sealed record Ping(int Count) :
                    DigitalBrain.Abstractions.Synapse;
            }
            """);

        string[] forbidden =
        [
            "Activator." + "CreateInstance",
            "Assembly." + "GetTypes",
            "App" + "Domain",
            "IGrain" + "Factory",
            "Grain" + "Id",
            "Neuron" + "Catalog",
        ];

        Assert.All(
            forbidden,
            token => Assert.DoesNotContain(
                token,
                generated.Source,
                StringComparison.Ordinal));
        Assert.Contains(
            "global::System.Int32.Parse(",
            generated.Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void UnsupportedConstructionFailsExplicitly()
    {
        var generated = Generate("""
            namespace Alpha
            {
                public sealed record Opaque(object Value) :
                    DigitalBrain.Abstractions.Synapse;
            }
            """);

        Assert.Contains(
            "Cannot construct synapse 'Alpha.Opaque'",
            generated.Source,
            StringComparison.Ordinal);
        Assert.Contains(
            "NotSupportedException",
            generated.Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MutablePropertyFactoriesParseTheCapturedValue()
    {
        var generated = Generate("""
            namespace Alpha
            {
                public sealed record Mutable :
                    DigitalBrain.Abstractions.Synapse
                {
                    public int Count { get; set; }
                }
            }
            """);

        Assert.Contains(
            "global::System.Int32.Parse(valueCount, global::System.Globalization.CultureInfo.InvariantCulture)",
            generated.Source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Argument(arguments, \"valueCount\")",
            generated.Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void UnsupportedWritablePropertyShapesFailExplicitly()
    {
        var generated = Generate("""
            namespace Alpha
            {
                public sealed record InitOnly :
                    DigitalBrain.Abstractions.Synapse
                {
                    public string Value { get; init; } = "";
                }

                public sealed record OpaqueProperty :
                    DigitalBrain.Abstractions.Synapse
                {
                    public object Value { get; set; } = new object();
                }

                public sealed record Indexed :
                    DigitalBrain.Abstractions.Synapse
                {
                    public string this[int index]
                    {
                        get => "";
                        set { }
                    }
                }
            }
            """);

        Assert.Contains(
            "Cannot construct synapse 'Alpha.InitOnly'",
            generated.Source,
            StringComparison.Ordinal);
        Assert.Contains(
            "Cannot construct synapse 'Alpha.OpaqueProperty'",
            generated.Source,
            StringComparison.Ordinal);
        Assert.Contains(
            "Cannot construct synapse 'Alpha.Indexed'",
            generated.Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void VocabularyIncludesOnlyEffectivelyPublicClosedTypes()
    {
        var generated = Generate("""
            namespace Alpha
            {
                internal static class HiddenContainer
                {
                    public sealed record HiddenSignal :
                        DigitalBrain.Abstractions.Synapse;
                }

                public static class GenericContainer<T>
                {
                    public sealed record GenericSignal :
                        DigitalBrain.Abstractions.Synapse;
                }

                public static class PublicContainer
                {
                    public sealed record VisibleSignal :
                        DigitalBrain.Abstractions.Synapse;
                }
            }
            """);

        Assert.DoesNotContain(
            "HiddenSignal",
            generated.Source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "GenericSignal",
            generated.Source,
            StringComparison.Ordinal);
        Assert.Contains(
            "VisibleSignal",
            generated.Source,
            StringComparison.Ordinal);
    }

    private static GeneratedVocabulary Generate(string vocabulary)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(
            Framework + Environment.NewLine + vocabulary,
            CSharpParseOptions.Default.WithLanguageVersion(
                LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create(
            "VocabularyConsumer",
            [syntaxTree],
            TrustedPlatformReferences(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new DispatchManifestGenerator().AsSourceGenerator());

        driver = driver.RunGenerators(compilation);
        var result = Assert.Single(driver.GetRunResult().Results);
        var source = Assert.Single(
            result.GeneratedSources,
            generated => generated.HintName == "GeneratedTestVocabulary.g.cs");

        return new GeneratedVocabulary(
            source.SourceText.ToString(),
            result.Diagnostics);
    }

    private static PortableExecutableReference[] TrustedPlatformReferences()
        => ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToArray();

    private sealed record GeneratedVocabulary(
        string Source,
        ImmutableArray<Diagnostic> Diagnostics);
}
