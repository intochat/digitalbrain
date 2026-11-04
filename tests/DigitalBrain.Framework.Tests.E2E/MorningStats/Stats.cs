using DigitalBrain.Time;
using DigitalBrain.UI;

namespace DigitalBrain.Framework.Tests.E2E.MorningStats;

[Signal("morning.report-ready")]
public sealed record ReportReady(DateOnly Day, int Orders, decimal Revenue) : Signal;

[State("stats")]
public sealed record StatsState(DateOnly? Day = null, int Orders = 0, decimal Revenue = 0)
{
    public StatsState() : this(null, 0, 0) { }
}

// The design's example: one file whose signatures say what it needs (a read-only SQL slot, a daily schedule, a reaction, a view).
public static class MorningStats
{
    [Synapse(from: typeof(Tick), into: "stats", Every = "07:00")]
    public static async Task<Reaction<StatsState>> Refresh(Tick t, StatsState s, [Slot] ISqlRead Db, CancellationToken ct)
    {
        var day = DateOnly.FromDateTime(t.At.UtcDateTime.Date).AddDays(-1);
        var orders = await Db.Scalar<int>("select count(*) from orders where day = @day", new { day }, ct);
        var revenue = await Db.Scalar<decimal>("select sum(total) from orders where day = @day", new { day }, ct);
        return Reaction<StatsState>.Of(new StatsState(day, orders, revenue), new ReportReady(day, orders, revenue));
    }

    [Synapse(from: "stats")]
    public static View Show(StatsState s) =>
        new("Yesterday", new Column([new Chart("columns", "Revenue", [new ChartPointNode(s.Day?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? "-", (double)s.Revenue)], null, "Revenue")]));
}
