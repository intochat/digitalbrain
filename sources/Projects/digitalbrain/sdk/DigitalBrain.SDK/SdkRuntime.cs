namespace DigitalBrain.SDK;

/// <summary>
/// Establish a global static context for the DigitalBrain SDK to support highly expressive APIs
/// like Llm.Prompt("...") without needing explicit dependency injection in every script or scratchpad.
/// </summary>
public static class SdkRuntime
{
    public static IServiceProvider? ServiceProvider { get; set; }
    public static IGrainFactory? GrainFactory { get; set; }
}
