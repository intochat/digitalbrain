namespace DigitalBrain.Time;

[Alias("time.alarm")]
internal interface ITimerAlarm : IGrainWithStringKey
{
    [Alias("arm")]
    Task Arm(TimeSpan dueIn);

    [Alias("retire")]
    Task Retire();
}
