namespace DigitalBrain.Time;

// Pure phase-preserving catch-up math (testable without a silo).
internal static class ScheduleCatchUp
{
    public static (int Collapsed, DateTimeOffset NextDue, ScheduleResolution Resolution) Compute(
        DateTimeOffset nextDue,
        DateTimeOffset observedAt,
        TimeSpan period,
        TimeSpan reminderPeriod)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(period.Ticks, 0);

        if (observedAt < nextDue)
        {
            throw new ArgumentException("Observed time is still before NextDue; no collapse.", nameof(observedAt));
        }

        var late = observedAt - nextDue;
        var collapsed = (int)(late.Ticks / period.Ticks) + 1;
        if (collapsed < 1)
        {
            collapsed = 1;
        }

        var dueAt = nextDue;
        var advanced = dueAt + TimeSpan.FromTicks(period.Ticks * collapsed);
        var resolution = collapsed > 1 || observedAt > dueAt + reminderPeriod
            ? ScheduleResolution.Recovered
            : ScheduleResolution.OnTime;

        return (collapsed, advanced, resolution);
    }
}
