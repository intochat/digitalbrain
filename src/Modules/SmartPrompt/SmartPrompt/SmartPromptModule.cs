namespace DigitalBrain.SmartPrompt;

public sealed class SmartPromptModule : Core.IModule
{
    public void Configure(ISiloBuilder builder)
        => ArgumentNullException.ThrowIfNull(builder);
}
