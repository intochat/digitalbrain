using DigitalBrain.Analyzers.Synapses;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;

namespace DigitalBrain.Analyzers.Tests.Unit.Synapses;

public sealed class StatusStringAnalyzerFacts
{
    private const string Prelude = """
        using System.Threading.Tasks;
        using DigitalBrain;
        using DigitalBrain.Rules;
        [Signal("test.charging")] public sealed record Charging(decimal Amount) : Signal;
        public enum Phase { Open, Closed }
        """;

    private const string React = "[Synapse(typeof(Charging), \"week\")] public static WeekSpend React(Charging s, WeekSpend w) => w;";

    private static string Source(string state, string method) => Prelude + "\n" + state + "\npublic static class Reactions\n{\n    " + method + "\n}\n";

    [Theory]
    [InlineData("[State(\"week\")] public sealed record WeekSpend(decimal Spent = 0, string {|#0:Status|} = \"\");", "Status")]
    [InlineData("[State(\"week\")] public sealed record WeekSpend(decimal Spent = 0) { public bool {|#0:Failed|} { get; init; } }", "Failed")]
    [InlineData("[State(\"week\")] public sealed record WeekSpend(decimal Spent = 0) { public Phase {|#0:ValidationStatus|} { get; init; } }", "ValidationStatus")]
    [InlineData("[State(\"week\")] public sealed record WeekSpend(decimal Spent = 0, string {|#0:LastError|} = \"\");", "LastError")]
    public Task A_status_like_member_on_a_returned_state_is_reported(string state, string member)
    {
        var test = Verify<StatusStringAnalyzer>.Create(Source(state, React));
        test.ExpectedDiagnostics.Add(Expected(member));
        return test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public Task A_reaction_returning_Reaction_of_state_is_reported()
    {
        var test = Verify<StatusStringAnalyzer>.Create(Source(
            "[State(\"week\")] public sealed record WeekSpend(decimal Spent = 0, bool {|#0:Rejected|} = false);",
            "[Synapse(typeof(Charging), \"week\")] public static Task<Reaction<WeekSpend>> React(Charging s, WeekSpend w) => default!;"));
        test.ExpectedDiagnostics.Add(Expected("Rejected"));
        return test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public Task A_state_is_reported_once_however_many_reactions_return_it()
    {
        var test = Verify<StatusStringAnalyzer>.Create(Source(
            "[State(\"week\")] public sealed record WeekSpend(string {|#0:Status|} = \"\", bool Failed = false);",
            "[Synapse(typeof(Charging), \"week\")] public static WeekSpend A(Charging s, WeekSpend w) => w;\n    [Synapse(typeof(Charging), \"week\")] public static WeekSpend B(Charging s, WeekSpend w) => w;"));
        test.ExpectedDiagnostics.Add(Expected("Status"));
        return test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("[State(\"week\")] public sealed record WeekSpend(decimal Spent = 0, string Note = \"\", Phase Stage = default, int Validated = 0);")]
    [InlineData("[State(\"week\")] public sealed record WeekSpend(decimal Spent = 0) { private bool failed; internal string Error { get; init; } = \"\"; }")]
    public Task Members_that_are_not_public_status_like_strings_bools_or_enums_are_ignored(string state) =>
        Verify<StatusStringAnalyzer>.Diagnostics(Source(state, React));

    [Fact]
    public Task A_gate_is_ignored() => Verify<StatusStringAnalyzer>.Diagnostics(Source(
        "[State(\"week\")] public sealed record WeekSpend(decimal Spent = 0, string Status = \"\");",
        "[Synapse(typeof(Charging), \"week\")] public static Check Gate(Charging s, WeekSpend w) => default!;"));

    [Fact]
    public Task A_view_is_ignored() => Verify<StatusStringAnalyzer>.Diagnostics(Source(
        "[State(\"week\")] public sealed record WeekSpend(decimal Spent = 0, string Status = \"\");",
        "[Synapse(\"week\")] public static string Show(WeekSpend w) => string.Empty;"));

    private static DiagnosticResult Expected(string member) =>
        new DiagnosticResult("DB0043", DiagnosticSeverity.Warning).WithLocation(0).WithArguments("WeekSpend", member);
}
