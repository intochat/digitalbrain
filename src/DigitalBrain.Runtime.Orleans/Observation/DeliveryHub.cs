using DigitalBrain.Kernel;

namespace DigitalBrain.Runtime.Orleans;

/// <summary>In-memory fan-out of synapse deliveries on this silo. A listener hears what is delivered after it subscribes.</summary>
internal sealed class DeliveryHub
{
    private readonly DeliverySubscriptions _subscriptions = new();

    /// <summary>Hands <paramref name="delivery"/> to the listeners of <paramref name="brain"/>.</summary>
    public void Publish(string brain, SignalDelivery delivery)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(brain);
        _subscriptions.Publish(brain, delivery);
    }

    /// <summary>Deliveries of <paramref name="brain"/> from now on.</summary>
    public IAsyncEnumerable<SignalDelivery> Listen(string brain, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(brain);
        return _subscriptions.Listen(brain, cancellationToken);
    }
}
