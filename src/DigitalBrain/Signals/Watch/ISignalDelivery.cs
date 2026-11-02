namespace DigitalBrain.Signals.Watch;

// In-process publish returns after it queues. A coalesce drain waits until those queued signals have
// reached the subscriber channel, so a burst posted during a rebuild is one following rebuild.
// Runtime SPI: implemented by in-process clients.
public interface ISignalDelivery
{
    Task DeliveredAsync(CancellationToken cancellationToken);
}
