namespace DigitalBrain.Time;

public sealed class TimeOptions
{
    public const string SectionName = "DigitalBrain:Time";

    // Orleans' minimum reminder period; hosts can override it when their reminder service permits.
    public TimeSpan AlarmPeriod { get; set; } = TimeSpan.FromMinutes(1);
}
