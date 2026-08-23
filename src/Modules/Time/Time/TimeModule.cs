namespace DigitalBrain.Time;

// Time has no container registration of its own today, but this is its explicit product-module
// identity: AppHost selects it and the Kernel activates only that selected manifest.
public sealed class TimeModule : Core.IModule
{
    public void Configure(ISiloBuilder builder)
        => ArgumentNullException.ThrowIfNull(builder);
}
