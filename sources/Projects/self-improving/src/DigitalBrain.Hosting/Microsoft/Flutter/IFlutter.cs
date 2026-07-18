using DigitalBrain.Protocol;

namespace DigitalBrain.Hosting.Microsoft.Flutter;

public interface IFlutter : INeuron,
    IHandle<StartFlutterClient>,
    IEmit<FlutterClientStarted>
{
    // Fire-and-forget command emission (timeline/journal like every synapse). Grain starts the renderer for live UiSurface over gRPC.
    Task StartFlutterClientAsync(string target = "web-server", CancellationToken cancellationToken = default);
}