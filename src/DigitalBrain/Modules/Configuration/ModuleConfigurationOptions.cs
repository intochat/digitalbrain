namespace DigitalBrain.Modules.Configuration;

public sealed class ModuleConfigurationOptions
{
    public string Root { get; init; } = "DigitalBrain";

    public static ModuleConfigurationOptions Current { get; set; } = new();
}
