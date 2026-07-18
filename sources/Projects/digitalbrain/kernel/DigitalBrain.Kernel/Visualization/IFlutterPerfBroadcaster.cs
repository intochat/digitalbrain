using DigitalBrain.Runtime.Ui;

namespace DigitalBrain.Kernel.Visualization;

// Thin neuron over HomeFeedBus so FlutterPerfNeuron tests can capture broadcasts
// without standing up a real bus.
public interface IFlutterPerfBroadcaster
{
    Task BroadcastAsync(RfwCard card, CancellationToken cancellationToken = default);
}

internal sealed class FlutterPerfBroadcaster(Gateway.HomeFeedBus bus) : IFlutterPerfBroadcaster
{
    public Task BroadcastAsync(RfwCard card, CancellationToken cancellationToken = default)
        => bus.BroadcastAsync(card, cancellationToken);
}
