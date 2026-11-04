using DigitalBrain.Analyzers.Synapses;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;

namespace DigitalBrain.Analyzers.Tests.Unit.Synapses;

public sealed class StateLocalityAnalyzerFacts
{
    private const string Prelude = """
        using DigitalBrain;
        using DigitalBrain.Rules;
        [Signal("test.charging")] public sealed record Charging(decimal Amount) : Signal;
        """;

    // The state lives in a second assembly that declares only the [State] attribute it needs.
    private const string Library = """
        namespace DigitalBrain { [System.AttributeUsage(System.AttributeTargets.Class)] public sealed class StateAttribute(string alias) : System.Attribute { public string Alias { get; } = alias; } }
        [DigitalBrain.State("week")] public sealed record WeekSpend(decimal Spent = 0);
        """;

    [Fact]
    public Task State_declared_in_the_same_assembly_reports_nothing() => Verify<StateLocalityAnalyzer>.Diagnostics(Prelude + """

        [State("week")] public sealed record WeekSpend(decimal Spent = 0);
        public static class Gates
        {
            [Synapse(typeof(Charging), "week")] public static Check Cap(Charging s, WeekSpend w) => default!;
        }
        """);

    [Fact]
    public Task State_declared_in_another_assembly_is_refused()
    {
        var test = Verify<StateLocalityAnalyzer>.Create(Prelude + """

            public static class Gates
            {
                [Synapse(typeof(Charging), "week")] public static Check {|#0:Cap|}(Charging s, WeekSpend w) => default!;
            }
            """);
        test.TestState.AdditionalProjects["Lib"].Sources.Add(("Lib.cs", Library));
        test.TestState.AdditionalProjectReferences.Add("Lib");
        test.ExpectedDiagnostics.Add(new DiagnosticResult("DB0042", DiagnosticSeverity.Error).WithLocation(0).WithArguments("Gates.Cap", "WeekSpend"));
        return test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public Task A_method_without_the_attribute_is_ignored()
    {
        var test = Verify<StateLocalityAnalyzer>.Create(Prelude + """

            public static class Gates
            {
                public static Check Cap(Charging s, WeekSpend w) => default!;
            }
            """);
        test.TestState.AdditionalProjects["Lib"].Sources.Add(("Lib.cs", Library));
        test.TestState.AdditionalProjectReferences.Add("Lib");
        return test.RunAsync(TestContext.Current.CancellationToken);
    }
}
