using DigitalBrain.Runtime.Ui;

namespace DigitalBrain.Kernel.Visualization;

// Thin neuron over HomeFeedBus so TaskManagerNeuron tests can capture broadcasts
// without standing up a real bus (which requires IGrainFactory + IConversation).
public interface ITaskManagerBroadcaster
{
    Task BroadcastAsync(RfwCard card, CancellationToken cancellationToken = default);
}

public sealed class HomeFeedBroadcaster(Gateway.HomeFeedBus bus) : ITaskManagerBroadcaster
{
    public Task BroadcastAsync(RfwCard card, CancellationToken cancellationToken = default)
        => bus.BroadcastAsync(card, cancellationToken);
}
