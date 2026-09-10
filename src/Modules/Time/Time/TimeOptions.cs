namespace DigitalBrain.Time;

public sealed class TimeOptions
{
    // Orleans' minimum reminder period; hosts can override it when their reminder service permits.
    public TimeSpan AlarmPeriod { get; init; } = TimeSpan.FromMinutes(1);
}
