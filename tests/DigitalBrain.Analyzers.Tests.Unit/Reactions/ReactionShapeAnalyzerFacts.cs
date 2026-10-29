using DigitalBrain.Analyzers.Reactions;

namespace DigitalBrain.Analyzers.Tests.Unit.Reactions;

public sealed class ReactionShapeAnalyzerFacts
{
    private const string Prelude = """
        using System.Threading;
        using System.Threading.Tasks;
        using DigitalBrain;
        public sealed record Number(int Value) : Signal;
        public sealed record Word(string Text) : Signal;
        """;

    [Fact]
    public Task Zero_parameters_reports_DB0014() => Verify<ReactionShapeAnalyzer>.Diagnostics(Prelude + """
        public sealed class L : Neuron { [On] public Task {|DB0014:Heard|}() => Task.CompletedTask; }
        """);

    [Fact]
    public Task Two_signal_parameters_reports_DB0014() => Verify<ReactionShapeAnalyzer>.Diagnostics(Prelude + """
        public sealed class L : Neuron { [On] public Task {|DB0014:Heard|}(Number n, Word w) => Task.CompletedTask; }
        """);

    [Fact]
    public Task Non_signal_parameter_reports_DB0014() => Verify<ReactionShapeAnalyzer>.Diagnostics(Prelude + """
        public sealed class L : Neuron { [On] public Task {|DB0014:Heard|}(int n) => Task.CompletedTask; }
        """);

    [Fact]
    public Task Void_return_reports_DB0014() => Verify<ReactionShapeAnalyzer>.Diagnostics(Prelude + """
        public sealed class L : Neuron { [On] public void {|DB0014:Heard|}(Number n) { } }
        """);

    [Fact]
    public Task Token_before_signal_or_generic_task_reports_DB0014() => Verify<ReactionShapeAnalyzer>.Diagnostics(Prelude + """
        public sealed class L : Neuron
        {
            [On] public Task {|DB0014:A|}(CancellationToken ct, Number n) => Task.CompletedTask;
            [On] public Task<int> {|DB0014:B|}(Number n) => Task.FromResult(1);
        }
        """);

    [Fact]
    public Task Well_formed_reactions_report_nothing() => Verify<ReactionShapeAnalyzer>.Diagnostics(Prelude + """
        public sealed class L : Neuron
        {
            [On] public Task Heard(Number n) => Task.CompletedTask;
            [On] public ValueTask Also(Number n, CancellationToken ct) => default;
            public void NotAReaction(int x) { }
        }
        """);
}
