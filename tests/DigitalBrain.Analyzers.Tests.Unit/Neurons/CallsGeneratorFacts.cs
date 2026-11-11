using DigitalBrain.Analyzers.Neurons;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DigitalBrain.Analyzers.Tests.Unit.Neurons;

public sealed class CallsGeneratorFacts
{
    private const string Usings = """
        using System.Threading.Tasks;
        using DigitalBrain;
        using DigitalBrain.Contracts;

        """;

    private const string Prelude = Usings + """
        namespace DigitalBrain
        {
            [System.AttributeUsage(System.AttributeTargets.Class)] public sealed class NeuronAttribute(string type) : System.Attribute { public string Type { get; } = type; }
            [System.AttributeUsage(System.AttributeTargets.Assembly, AllowMultiple = true)] public sealed class CallsAttribute(string from, string to) : System.Attribute { public string From { get; } = from; public string To { get; } = to; }
            public readonly record struct NeuronId(string Value);
            public abstract class Neuron { protected T Get<T>(NeuronId id) where T : class => default!; }
            public abstract class Screen { protected T Get<T>(string key) where T : class => default!; }
        }
        namespace DigitalBrain.Contracts
        {
            [System.AttributeUsage(System.AttributeTargets.Interface)] public sealed class NeuronAliasAttribute(string alias) : System.Attribute { public string Alias { get; } = alias; }
        }
        [NeuronAlias("ai.memory")] public interface IMemory { Task Remember(string text); }
        public interface IPlain { Task Go(); }
        """;

    private static string[] Generated(string source)
    {
        var compilation = CSharpCompilation.Create("calls",
            [CSharpSyntaxTree.ParseText(Prelude), CSharpSyntaxTree.ParseText(Usings + source)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location), MetadataReference.CreateFromFile(typeof(Task).Assembly.Location), MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("System.Runtime").Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var errors = compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()).ToArray();
        Assert.Empty(errors);
        var driver = CSharpGeneratorDriver.Create(new CallsGenerator()).RunGeneratorsAndUpdateCompilation(compilation, out _, out _, TestContext.Current.CancellationToken);
        var result = driver.GetRunResult();
        return [.. result.GeneratedTrees.SelectMany(tree => tree.GetText().Lines.Select(line => line.ToString())).Where(line => line.StartsWith("[assembly:", StringComparison.Ordinal))];
    }

    [Fact]
    public void A_neuron_calling_two_contracts_yields_two_calls_attributes_by_alias()
    {
        var lines = Generated("""
            [Neuron("tasks.engine")]
            public sealed class Engine : Neuron
            {
                public Task Run() => Get<IMemory>(default).Remember("x");
                public Task Go() => Get<IPlain>(default).Go();
            }
            """);

        Assert.Equal(
            ["""[assembly: global::DigitalBrain.Calls("tasks.engine", "IPlain")]""", """[assembly: global::DigitalBrain.Calls("tasks.engine", "ai.memory")]"""],
            lines);
    }

    [Fact]
    public void A_screen_is_recorded_by_its_id_and_duplicates_collapse()
    {
        var lines = Generated("""
            [Neuron("notes")]
            public sealed class Notes : Screen
            {
                public Task A() => Get<IMemory>("memory").Remember("a");
                public Task B() => Get<IMemory>("memory").Remember("b");
            }
            """);

        Assert.Equal(["""[assembly: global::DigitalBrain.Calls("notes", "ai.memory")]"""], lines);
    }

    [Fact]
    public void A_plain_class_or_another_Get_yields_nothing()
    {
        var lines = Generated("""
            public sealed class Helper { public T Get<T>() => default!; public void Run() => Get<IMemory>(); }
            public sealed class Unmarked : Neuron { public Task Run() => Get<IMemory>(default).Remember("x"); }
            """);

        Assert.Empty(lines);
    }
}
