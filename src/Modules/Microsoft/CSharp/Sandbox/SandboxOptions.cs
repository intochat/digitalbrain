namespace DigitalBrain.Microsoft.CSharp.Sandbox;

internal sealed class SandboxOptions
{
    public const string SectionName = "Sandbox";

    public string WorkRoot { get; set; } = "/work";
    // Every script references the brain client; development mounts the repository at /brain.
    public string ClientProject { get; set; } = "/brain/src/Modules/DigitalBrain/Kernel/Client/DigitalBrain.Client.csproj";
    public int LogLines { get; set; } = 5000;
}
