namespace DigitalBrain.UI;

public sealed class UIModule : Core.IModule
{
    public void Configure(ISiloBuilder builder)
        => ArgumentNullException.ThrowIfNull(builder);
}
