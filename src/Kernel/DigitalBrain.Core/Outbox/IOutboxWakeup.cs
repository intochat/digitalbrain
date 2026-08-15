namespace DigitalBrain.Core;

[Alias("db.outbox-wakeup")]
internal interface IOutboxWakeup : IGrainWithStringKey
{
    [Alias(nameof(Arm))]
    Task Arm();

    [Alias(nameof(Disarm))]
    Task Disarm();
}
