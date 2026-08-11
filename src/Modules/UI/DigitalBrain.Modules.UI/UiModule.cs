namespace DigitalBrain.UI;

public sealed class UiModule : Core.IModule
{
    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
    }
}
