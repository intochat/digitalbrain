using System.Diagnostics;
using System.Globalization;
using System.Text;
using DigitalBrain.Analyzers.Apps;
using DigitalBrain.Analyzers.Reactions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DigitalBrain.Analyzers.Tests.Unit.Apps;

// R4-06: the shipped Phase 3 DB0002 is the scheduling-attribute ban in App classes; these are its missing facts.
public sealed class AppSchedulingMemberFacts
{
    [Theory]
    [InlineData("Interleaves")]
    [InlineData("InterleavesWhen(\"P\")")]
    [InlineData("ReadOnlyCall")]
    [InlineData("OneWayCall")]
    [InlineData("CallTimeout(10)")]
    [InlineData("Reentrant")]
    public Task Each_attribute_on_an_app_property_reports_DB0002(string attribute) => Verify<AppSchedulingAnalyzer>.Diagnostics($$"""
        using DigitalBrain;
        public sealed class A : App
        {
            [{|DB0002:{{attribute}}|}] public int P { get; set; }
        }
        """);

    [Fact]
    public Task Attributes_on_methods_of_an_indirect_app_report_DB0002() => Verify<AppSchedulingAnalyzer>.Diagnostics("""
        using DigitalBrain;
        public abstract class Base : App { }
        public sealed class A : Base
        {
            [{|DB0002:Interleaves|}, {|DB0002:CallTimeout(5)|}] public void M() { }
            [{|DB0002:OneWayCall|}] public int F;
        }
        """);

    [Fact]
    public Task Look_alike_attribute_in_another_namespace_reports_nothing() => Verify<AppSchedulingAnalyzer>.Diagnostics("""
        namespace Mine
        {
            public sealed class ReentrantAttribute : System.Attribute;
            public sealed class ReadOnlyCallAttribute : System.Attribute;
            [Reentrant] public sealed class A : DigitalBrain.App
            {
                [ReadOnlyCall] public void R() { }
            }
        }
        """);

    [Fact]
    public Task Attributes_on_neuron_methods_report_nothing() => Verify<AppSchedulingAnalyzer>.Diagnostics("""
        using DigitalBrain;
        public sealed class N : Neuron
        {
            [Interleaves] public void I() { }
            [InterleavesWhen("P")] public void W() { }
            [OneWayCall] public void O() { }
        }
        """);
}

public sealed class AppRuleTimingFacts
{
    // T-04-07 timing criterion for the full app set: every DB rule (AnalyzerSet.All, 14 analyzers) over a generated 2 000-line app file.
    [Fact]
    public async Task Full_rule_set_handles_a_2000_line_app_file()
    {
        var source = new StringBuilder("""
            /*
            # Big

            Role: Example
            Shipped: false
            Operations: OPERATIONS
            */
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using DigitalBrain;
            using DigitalBrain.Capabilities;
            public sealed record Number(int Value) : Signal;

            """).Append(Verify<AppBaseAnalyzer>.Stubs).AppendLine();
        source.AppendLine("[App(\"big\")] public sealed class Big : App {");
        source.AppendLine("    private readonly string appKey = \"big\";");
        source.AppendLine("    private string state = \"\";");
        var bad = 0;
        var operations = new List<string>();
        for (var i = 0; source.ToString().Split('\n').Length < 2000; i++)
        {
            operations.Add("heard" + i.ToString(CultureInfo.InvariantCulture));
            source.AppendLine(CultureInfo.InvariantCulture, $"    [On] public Task Heard{i}(Number n) => Task.CompletedTask;");
            source.AppendLine(CultureInfo.InvariantCulture, $"    public string Label{i}(string folder) => folder + \"/x{i}\";");
            source.AppendLine(CultureInfo.InvariantCulture, $"    public void Guard{i}() {{ try {{ state = \"{i}\"; }} catch (FormatException e) {{ state = e.Message; }} }}");
            source.AppendLine(CultureInfo.InvariantCulture, $"    public CapabilityId Need{i}() => CapabilityId.Parse(\"storage@1\");");
            if (i % 50 == 0)
            {
                source.AppendLine(CultureInfo.InvariantCulture, $"    public string Key{i}() => appKey + \"/items/{i}\";");
                bad++;
            }
        }
        source.AppendLine("}");
        var text = source.ToString().Replace("OPERATIONS", string.Join(", ", operations), StringComparison.Ordinal);
        Assert.True(text.Split('\n').Length >= 2000);

        var references = await Microsoft.CodeAnalysis.Testing.ReferenceAssemblies.Net.Net80
            .ResolveAsync(LanguageNames.CSharp, TestContext.Current.CancellationToken);
        var compilation = CSharpCompilation.Create("Big", [CSharpSyntaxTree.ParseText(text, cancellationToken: TestContext.Current.CancellationToken)],
            references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error));

        var analyzers = AnalyzerSet.All;
        Assert.Equal(14, analyzers.Length);
        var watch = Stopwatch.StartNew();
        var diagnostics = await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);
        watch.Stop();

        Assert.Equal(bad, diagnostics.Count(d => d.Id == "DB0023"));
        Assert.Equal(bad, diagnostics.Length);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"analysis took {watch.Elapsed}");
    }
}
