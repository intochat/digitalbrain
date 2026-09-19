using DigitalBrain.Core;

namespace DigitalBrain.Flutter;

public sealed class FlutterModule : IModule
{
    public void Configure(ISiloBuilder silo)
    {
        ArgumentNullException.ThrowIfNull(silo);
    }
}
