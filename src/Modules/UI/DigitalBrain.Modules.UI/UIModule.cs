using DigitalBrain.Core;

namespace DigitalBrain.UI;

public sealed class UIModule : IModule
{
    public void Configure(ISiloBuilder builder) => ArgumentNullException.ThrowIfNull(builder);
}
