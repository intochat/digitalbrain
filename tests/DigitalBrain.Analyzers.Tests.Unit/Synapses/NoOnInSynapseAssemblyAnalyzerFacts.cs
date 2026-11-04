using DigitalBrain.Analyzers.Synapses;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;

namespace DigitalBrain.Analyzers.Tests.Unit.Synapses;

public sealed class NoOnInSynapseAssemblyAnalyzerFacts
{
    private const string State = """
        using DigitalBrain;
        [State("week")] public sealed record WeekSpend(decimal Spent = 0);
        """;

    [Fact]
    public Task An_On_method_beside_a_state_record_is_refused()
    {
        var test = Verify<NoOnInSynapseAssemblyAnalyzer>.Create(State + """

            public static class Reactions
            {
                [On] public static void {|#0:Roll|}() { }
            }
            """);
        test.ExpectedDiagnostics.Add(new DiagnosticResult("DB0044", DiagnosticSeverity.Error).WithLocation(0).WithArguments("Reactions.Roll"));
        return test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public Task An_On_method_in_an_assembly_without_state_is_ignored() => Verify<NoOnInSynapseAssemblyAnalyzer>.Diagnostics("""
        using DigitalBrain;
        public static class Reactions
        {
            [On] public static void Roll() { }
        }
        """);

    [Fact]
    public Task A_state_record_without_an_On_method_reports_nothing() => Verify<NoOnInSynapseAssemblyAnalyzer>.Diagnostics(State + """

        public static class Plain { public static void Roll() { } }
        """);
}
