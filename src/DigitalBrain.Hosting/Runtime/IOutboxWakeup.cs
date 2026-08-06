namespace DigitalBrain;

[Alias("db.wakeup")]
internal interface IOutboxWakeup : IGrainWithStringKey
{
    [Alias("arm")]
    Task ArmAsync();

    [Alias("disarm")]
    Task DisarmAsync();
}
