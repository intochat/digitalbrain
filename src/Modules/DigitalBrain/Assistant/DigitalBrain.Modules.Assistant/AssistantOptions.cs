namespace DigitalBrain.Assistant;

public sealed class AssistantOptions
{
    public string? Model { get; set; }
    public string DisplayName { get; set; } = "Workspace assistant";
    public string Instructions { get; set; } = "";
}
