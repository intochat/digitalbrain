using DigitalBrain.Runtime.Visualization;

namespace DigitalBrain.Kernel.Visualization;

public interface IFlutterPerfHintBroadcaster
{
    Task BroadcastAsync(VisualLoadHint hint, CancellationToken cancellationToken = default);

    IAsyncEnumerable<VisualLoadHint> SubscribeAsync(
        string clientId, CancellationToken cancellationToken);
}
