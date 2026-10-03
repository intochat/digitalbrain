namespace DigitalBrain.Microsoft.CSharp.Sandbox;

internal sealed class SandboxOptions
{
    public const string SectionName = "Sandbox";

    public string WorkRoot { get; set; } = "/work";
    // Every script references the brain client; development mounts the repository at /brain.
    public string ClientProject { get; set; } = "/brain/src/DigitalBrain/Client/DigitalBrain.Client/DigitalBrain.Client.csproj";
    public int LogLines { get; set; } = 5000;
    // Production sessions end when their container exits: with no run for this long, the host stops.
    public TimeSpan? IdleShutdown { get; set; }
}
