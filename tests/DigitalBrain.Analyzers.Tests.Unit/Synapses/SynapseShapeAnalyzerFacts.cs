using DigitalBrain.Analyzers.Synapses;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;

namespace DigitalBrain.Analyzers.Tests.Unit.Synapses;

public sealed class SynapseShapeAnalyzerFacts
{
    private const string Prelude = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using DigitalBrain;
        using DigitalBrain.Time;
        using DigitalBrain.Rules;
        [State("week")] public sealed record WeekSpend(decimal Spent = 0);
        [Signal("test.charging")] public sealed record Charging(decimal Amount) : Signal;
        [Capability("sql-read", 1)] public interface ISqlRead { }
        """;

    private const string NotPublicStatic = "a synapse function must be public static.";
    private const string UnknownParameter = "a parameter is not a signal, the state or CancellationToken.";

    private static string Wrap(string method) => Prelude + "\npublic sealed class Shapes\n{\n    " + method + "\n}\n";

    [Theory]
    [InlineData("[Synapse(typeof(Charging), \"week\")] public static Check {|#0:Gate|}(Charging s, WeekSpend w) => default!;")]
    [InlineData("[Synapse(typeof(Charging), \"week\")] public static Task<Check> {|#0:AsyncGate|}(Charging s, WeekSpend w, CancellationToken ct) => default!;")]
    [InlineData("[Synapse(typeof(Charging), \"week\")] public static WeekSpend {|#0:React|}(Charging s, WeekSpend w) => w;")]
    [InlineData("[Synapse(typeof(Charging), \"week\")] public static Reaction<WeekSpend> {|#0:ReactWithSignals|}(Charging s, WeekSpend w) => default!;")]
    [InlineData("[Synapse(typeof(Charging), \"week\")] public static Task<Reaction<WeekSpend>> {|#0:AsyncReact|}(Charging s, WeekSpend w) => default!;")]
    [InlineData("[Synapse(\"week\")] public static string {|#0:View|}(WeekSpend w) => string.Empty;")]
    [InlineData("[Synapse(typeof(Signal), \"week/2026-45\")] public static Check {|#0:KeyedGate|}(Signal s, WeekSpend w) => default!;")]
    [InlineData("[Synapse(typeof(Charging), \"week\")] public static Task<WeekSpend> {|#0:WithSlot|}(Charging s, WeekSpend w, [Slot] ISqlRead db) => default!;")]
    [InlineData("[Synapse(typeof(Charging), \"week\")] public static Task<WeekSpend> {|#0:SlotFirst|}(Charging s, [Slot(Name = \"Ledger\")] ISqlRead db, WeekSpend w, CancellationToken ct) => default!;")]
    [InlineData("[Synapse(typeof(Tick), \"week\", Every = \"07:00\")] public static WeekSpend {|#0:Daily|}(Tick t, WeekSpend w) => w;")]
    [InlineData("[Synapse(typeof(Tick), \"week\", Every = \"Mon 07:00\")] public static WeekSpend {|#0:Weekly|}(Tick t, WeekSpend w) => w;")]
    [InlineData("[Synapse(typeof(Tick), \"week\", Every = \"every 5m\")] public static WeekSpend {|#0:Interval|}(Tick t, WeekSpend w) => w;")]
    public Task Recognised_shapes_report_nothing(string method) => Run(method, null);

    [Theory]
    [InlineData("[Synapse(\"week\")] public static Check {|#0:NoSignal|}(WeekSpend w) => default!;", "NoSignal",
        "a gate needs a signal (a reaction as well), declared by [Synapse] and taken as a parameter.")]
    [InlineData("[Synapse(typeof(Charging), \"week\")] public static Check {|#0:TwoSignals|}(Charging a, Charging b, WeekSpend w) => default!;", "TwoSignals",
        "a synapse takes at most one signal.")]
    [InlineData("[Synapse(typeof(Charging), \"week\")] public static Check {|#0:WrongState|}(Charging s, string w) => default!;", "WrongState",
        "a synapse takes exactly one state record 'week' parameter.")]
    [InlineData("[Synapse(typeof(Charging), \"gone\")] public static Check {|#0:UnknownState|}(Charging s, WeekSpend w) => default!;", "UnknownState",
        "a synapse takes exactly one state record 'gone' parameter.")]
    [InlineData("[Synapse(typeof(Charging), \"week\")] public static string {|#0:ViewWithSignal|}(Charging s, WeekSpend w) => string.Empty;", "ViewWithSignal",
        "a view takes only the state; a function with a signal must return Check or the state.")]
    [InlineData("[Synapse(typeof(Charging), \"week\")] public Check {|#0:Instance|}(Charging s, WeekSpend w) => default!;", "Instance", NotPublicStatic)]
    [InlineData("[Synapse(typeof(Charging), \"week\")] public static Check {|#0:TokenFirst|}(CancellationToken ct, Charging s, WeekSpend w) => default!;", "TokenFirst", UnknownParameter)]
    [InlineData("[Synapse(typeof(Charging), \"week\")] public static Check {|#0:TwoTokens|}(Charging s, WeekSpend w, CancellationToken a, CancellationToken b) => default!;", "TwoTokens", UnknownParameter)]
    [InlineData("[Synapse(typeof(Charging), \"week\")] public static Check {|#0:WithServices|}(Charging s, WeekSpend w, IServiceProvider sp) => default!;", "WithServices", UnknownParameter)]
    [InlineData("[Synapse(typeof(Signal), \"week\")] public static Check {|#0:NarrowParameter|}(Charging s, WeekSpend w) => default!;", "NarrowParameter",
        "the declared signal type must be assignable to the signal parameter.")]
    [InlineData("[Synapse(typeof(Charging), \"week\")] public static WeekSpend {|#0:NotAContract|}(Charging s, WeekSpend w, [Slot] string notAContract) => w;", "NotAContract",
        "a slot contract needs [Capability].")]
    [InlineData("[Synapse(typeof(Tick), \"week\", Every = \"7pm\")] public static WeekSpend {|#0:BadSchedule|}(Tick t, WeekSpend w) => w;", "BadSchedule",
        "a schedule: 7pm is not HH:mm, Mon HH:mm, every 5m or every 2h.")]
    [InlineData("[Synapse(typeof(Tick), \"week\", Every = \"every 0m\")] public static WeekSpend {|#0:ZeroInterval|}(Tick t, WeekSpend w) => w;", "ZeroInterval",
        "a schedule: every 0m is not HH:mm, Mon HH:mm, every 5m or every 2h.")]
    [InlineData("[Synapse(typeof(Charging), \"week\", Every = \"07:00\")] public static WeekSpend {|#0:EveryOnCharging|}(Charging s, WeekSpend w) => w;", "EveryOnCharging",
        "an every needs a Tick reaction.")]
    [InlineData("[Synapse(typeof(Tick), \"week\", Every = \"07:00\")] public static Check {|#0:EveryOnGate|}(Tick t, WeekSpend w) => default!;", "EveryOnGate",
        "an every needs a Tick reaction.")]
    [InlineData("[Synapse(\"week\", Every = \"07:00\")] public static string {|#0:EveryOnView|}(WeekSpend w) => string.Empty;", "EveryOnView",
        "an every needs a Tick reaction.")]
    public Task Refused_shapes_report_DB0041_with_the_reason(string method, string name, string reason) => Run(method, ("Shapes." + name, reason));

    [Fact]
    public Task A_method_nested_in_a_non_static_class_is_refused()
    {
        var test = Verify<SynapseShapeAnalyzer>.Create(Prelude + """

            public sealed class Outer
            {
                public static class Inner
                {
                    [Synapse(typeof(Charging), "week")] public static Check {|#0:Gate|}(Charging s, WeekSpend w) => default!;
                }
            }
            """);
        test.ExpectedDiagnostics.Add(Expect("Outer.Inner.Gate", "a synapse function must be public static on a static class or a top-level type, not nested in a non-static class."));
        return test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public Task A_method_without_the_attribute_is_ignored() => Verify<SynapseShapeAnalyzer>.Diagnostics(Wrap("public Check NotASynapse(WeekSpend w) => default!;"));

    [Fact]
    public void Reason_fragments_are_the_specified_text()
    {
        Assert.Equal("gate needs a signal", Rules.SynapseReasons.GateNeedsSignal);
        Assert.Equal("at most one signal", Rules.SynapseReasons.AtMostOneSignal);
        Assert.Equal("state record", Rules.SynapseReasons.StateRecord);
        Assert.Equal("view takes only the state", Rules.SynapseReasons.ViewTakesOnlyState);
        Assert.Equal("public static", Rules.SynapseReasons.PublicStatic);
        Assert.Equal("not a signal, the state or CancellationToken", Rules.SynapseReasons.UnknownParameter);
        Assert.Equal("declared signal type must be assignable to the signal parameter", Rules.SynapseReasons.SignalFitsParameter);
        Assert.Equal("slot contract needs [Capability]", Rules.SynapseReasons.SlotContractNeedsCapability);
        Assert.Equal("schedule", Rules.SynapseReasons.Schedule);
        Assert.Equal("every needs a Tick reaction", Rules.SynapseReasons.EveryNeedsTick);
    }

    private static DiagnosticResult Expect(string name, string reason) =>
        new DiagnosticResult("DB0041", DiagnosticSeverity.Error).WithLocation(0).WithArguments(name, reason);

    private static Task Run(string method, (string Name, string Reason)? refused)
    {
        var test = Verify<SynapseShapeAnalyzer>.Create(Wrap(method));
        if (refused is { } r) { test.ExpectedDiagnostics.Add(Expect(r.Name, r.Reason)); }
        return test.RunAsync(TestContext.Current.CancellationToken);
    }
}
