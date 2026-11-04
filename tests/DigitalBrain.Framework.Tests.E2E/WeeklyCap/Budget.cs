using DigitalBrain.Rules;
using DigitalBrain.UI;

namespace DigitalBrain.Framework.Tests.E2E.WeeklyCap;

[State("week")]
public sealed record WeekSpend(DateOnly Week = default, decimal Spent = 0)
{
    public WeekSpend() : this(default, 0) { }
}

public static class Budget
{
    [Synapse(typeof(Charging), "week")]
    public static Check WeeklyCap(Charging s, WeekSpend t) =>
        t.Spent + s.Amount > 300m
            ? Check.Reject("weekly cap of $300")
            : Check.Allow(t with { Spent = t.Spent + s.Amount });

    [Synapse(typeof(WeekRolled), "week")]
    public static WeekSpend NewWeek(WeekRolled s, WeekSpend t) => new(s.Week);

    [Synapse("week")]
    public static View Show(WeekSpend t) =>
        new("This week", new Column([new StatCardNode("spent", "Spent", $"{t.Spent} of 300")]));
}
