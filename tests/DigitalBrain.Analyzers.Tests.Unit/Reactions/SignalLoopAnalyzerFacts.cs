using System.Diagnostics;
using System.Globalization;
using System.Text;
using DigitalBrain.Analyzers.Reactions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DigitalBrain.Analyzers.Tests.Unit.Reactions;

public sealed class SignalLoopAnalyzerFacts
{
    private const string Prelude = """
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using DigitalBrain;
        public sealed record Number(int Value) : Signal;
        """;

    [Fact]
    public Task Signal_loop_in_neuron_reports_DB0013() => Verify<SignalLoopAnalyzer>.Diagnostics(Prelude + """
        public sealed class Listener : Neuron
        {
            private IAsyncEnumerable<T> Signals<T>() where T : Signal => throw null!;
            public async Task Run() { {|DB0013:await foreach (var n in Signals<Number>()) { }|} }
        }
        """);

    [Fact]
    public Task Signal_loop_over_any_async_enumerable_in_app_reports_DB0013() => Verify<SignalLoopAnalyzer>.Diagnostics(Prelude + """
        public abstract class Base : App { }
        public sealed class Mail : Base
        {
            public async Task Run(IAsyncEnumerable<Number> numbers) { {|DB0013:await foreach (var n in numbers) { }|} }
        }
        """);

    [Fact]
    public Task Non_signal_loop_in_neuron_reports_nothing() => Verify<SignalLoopAnalyzer>.Diagnostics(Prelude + """
        public sealed class Listener : Neuron
        {
            public async Task Run(IAsyncEnumerable<int> values) { await foreach (var v in values) { } }
        }
        """);

    [Fact]
    public Task Signal_loop_in_plain_helper_reports_nothing() => Verify<SignalLoopAnalyzer>.Diagnostics(Prelude + """
        public sealed class Helper
        {
            public async Task Run(IAsyncEnumerable<Number> numbers) { await foreach (var n in numbers) { } }
        }
        """);

    [Fact]
    public Task Plain_foreach_over_signals_reports_nothing() => Verify<SignalLoopAnalyzer>.Diagnostics(Prelude + """
        public sealed class Listener : Neuron
        {
            public void Run(List<Number> numbers) { foreach (var n in numbers) { } }
        }
        """);

    // T-04-07: both reaction analyzers over a generated 2 000-line neuron file finish well inside the test timeout.
    [Fact]
    public async Task Both_analyzers_handle_a_2000_line_neuron_file()
    {
        var source = new StringBuilder(Prelude).Append(Verify<SignalLoopAnalyzer>.Stubs).AppendLine();
        source.AppendLine("public sealed class Big : Neuron {");
        source.AppendLine("    private IAsyncEnumerable<T> Signals<T>() where T : Signal => throw null!;");
        var loops = 0;
        for (var i = 0; source.ToString().Split('\n').Length < 2000; i++)
        {
            source.AppendLine(CultureInfo.InvariantCulture, $"    [On] public Task Heard{i}(Number n) => Task.CompletedTask;");
            source.AppendLine(CultureInfo.InvariantCulture, $"    public async Task Loop{i}(IAsyncEnumerable<int> values) {{ await foreach (var v in values) {{ }} }}");
            if (i % 50 == 0) { source.AppendLine(CultureInfo.InvariantCulture, $"    public async Task Bad{i}() {{ await foreach (var n in Signals<Number>()) {{ }} }}"); loops++; }
        }
        source.AppendLine("}");
        var text = source.ToString();
        Assert.True(text.Split('\n').Length >= 2000);

        var references = await Microsoft.CodeAnalysis.Testing.ReferenceAssemblies.Net.Net80
            .ResolveAsync(LanguageNames.CSharp, TestContext.Current.CancellationToken);
        var compilation = CSharpCompilation.Create("Big", [CSharpSyntaxTree.ParseText(text, cancellationToken: TestContext.Current.CancellationToken)],
            references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error));

        var watch = Stopwatch.StartNew();
        var diagnostics = await compilation.WithAnalyzers([new SignalLoopAnalyzer(), new ReactionShapeAnalyzer()])
            .GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);
        watch.Stop();

        Assert.Equal(loops, diagnostics.Count(d => d.Id == "DB0013"));
        Assert.DoesNotContain(diagnostics, d => d.Id == "DB0014");
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"analysis took {watch.Elapsed}");
    }
}
