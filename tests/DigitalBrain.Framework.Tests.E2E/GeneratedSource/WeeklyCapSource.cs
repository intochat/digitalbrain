namespace DigitalBrain.Framework.Tests.E2E.GeneratedSource;

// The design's weekly-cap file as text, with its own aliases. Everything it uses is declared in the text itself (the Charging
// signal included), so the test publisher reads the live generated signal type from the catalog instead of naming it.
internal static class WeeklyCapSource
{
    internal const string Key = "Me.GeneratedBudget";

    internal const string Week = "gs.week";

    internal const string Budget = """
        namespace Me.GeneratedBudget;

        using DigitalBrain.Modules;
        using DigitalBrain.UI;

        [Signal("gs.charging")]
        public sealed record Charging(decimal Amount) : Signal;

        [Signal("gs.week-rolled")]
        public sealed record WeekRolled(DateOnly Week) : Signal;

        [Signal("gs.report-ready")]
        public sealed record ReportReady(DateOnly Week, decimal Spent) : Signal;

        [State("gs.week")]
        public sealed record WeekSpend(DateOnly Week = default, decimal Spent = 0)
        {
            public WeekSpend() : this(default, 0) { }
        }

        public static class Budget
        {
            [Synapse(typeof(Charging), "gs.week")]
            public static WeekSpend WeeklyCap(Charging s, WeekSpend t) =>
                t with { Spent = t.Spent + s.Amount };

            [Synapse(typeof(WeekRolled), "gs.week")]
            public static Reaction<WeekSpend> NewWeek(WeekRolled s, WeekSpend t) =>
                Reaction<WeekSpend>.Of(new WeekSpend(s.Week, 0), new ReportReady(t.Week, t.Spent));

            [Synapse("gs.week")]
            public static View Show(WeekSpend t) =>
                new("This week", new Column([new StatCardNode("spent", "Spent", $"{t.Spent} of 300")]));
        }
        """;

    // Need-neutral: only the spend accumulation changes.
    internal static readonly string Cap200 = Budget.Replace("Spent = t.Spent + s.Amount", "Spent = t.Spent + s.Amount * 2", StringComparison.Ordinal);

    // A new need: the reaction takes a sql-read slot (the ISqlRead contract of the morning-stats facts).
    internal static readonly string WithSlot = Budget
        .Replace("namespace Me.GeneratedBudget;", "using DigitalBrain.Framework.Tests.E2E.MorningStats;\n\nnamespace Me.GeneratedBudget;", StringComparison.Ordinal)
        .Replace("WeeklyCap(Charging s, WeekSpend t)", "WeeklyCap(Charging s, WeekSpend t, [Slot] ISqlRead Db)", StringComparison.Ordinal);
}
