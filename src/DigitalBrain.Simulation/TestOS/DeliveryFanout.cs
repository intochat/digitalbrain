using DigitalBrain.Kernel;

namespace DigitalBrain.Testing;

internal sealed class DeliveryFanout
{
    private readonly DeliverySubscriptions _subscriptions = new();

    public void Publish(SignalDelivery delivery) => _subscriptions.Publish(null, delivery);

    public IAsyncEnumerable<SignalDelivery> Listen(CancellationToken cancellationToken) => _subscriptions.Listen(null, cancellationToken);
}
