using Ino.Core.Hosting;

namespace Ino.Core.Capabilities;

public interface ICortexCapability
{
    Task<RoutingResult> RouteAsync(string prompt, NeuronContext ctx, CancellationToken ct);
}
