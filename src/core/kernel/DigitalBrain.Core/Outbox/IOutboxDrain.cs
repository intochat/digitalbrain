namespace DigitalBrain.Core;

[Alias("db.outbox-drain")]
internal interface IOutboxDrain : IGrainWithStringKey
{
    [Alias(nameof(Drain))]
    Task Drain();
}
