using DigitalBrain.Time;

namespace DigitalBrain.Modules.Time.Tests;

public sealed class ScheduleCatchUpTests
{
    private static readonly TimeSpan ReminderPeriod = TimeSpan.FromMinutes(1);

    [Fact]
    public void OnTime_single_period_collapse_is_one()
    {
        var nextDue = new DateTimeOffset(2026, 8, 12, 10, 0, 0, TimeSpan.Zero);
        var observed = nextDue.AddSeconds(5);
        var period = TimeSpan.FromMinutes(15);

        var (collapsed, advanced, resolution) = ScheduleCatchUp.Compute(
            nextDue,
            observed,
            period,
            ReminderPeriod);

        Assert.Equal(1, collapsed);
        Assert.Equal(nextDue + period, advanced);
        Assert.Equal(ScheduleResolution.OnTime, resolution);
    }

    [Fact]
    public void Missed_four_periods_collapses_to_four()
    {
        var nextDue = new DateTimeOffset(2026, 8, 12, 10, 0, 0, TimeSpan.Zero);
        var period = TimeSpan.FromMinutes(15);
        // Exactly 4 full periods late → late/period = 4, +1 inclusive? 
        // late = 4*period → collapsed = 4 + 1 = 5 if exact boundary.
        // Force catch-up gate: backdate by MissedPeriods - exercise collapsed=4.
        // late.Ticks / period.Ticks + 1 with late = 3*period + epsilon → 4
        var observed = nextDue + (period * 3) + TimeSpan.FromSeconds(1);

        var (collapsed, advanced, resolution) = ScheduleCatchUp.Compute(
            nextDue,
            observed,
            period,
            ReminderPeriod);

        Assert.Equal(4, collapsed);
        Assert.Equal(nextDue + (period * 4), advanced);
        Assert.Equal(ScheduleResolution.Recovered, resolution);
    }

    [Fact]
    public void Zero_or_negative_period_throws()
    {
        var nextDue = DateTimeOffset.UtcNow;
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ScheduleCatchUp.Compute(nextDue, nextDue, TimeSpan.Zero, ReminderPeriod));
    }

    [Fact]
    public void Observed_before_next_due_throws()
    {
        var nextDue = DateTimeOffset.UtcNow;
        Assert.Throws<ArgumentException>(() =>
            ScheduleCatchUp.Compute(
                nextDue,
                nextDue.AddSeconds(-1),
                TimeSpan.FromMinutes(1),
                ReminderPeriod));
    }
}
