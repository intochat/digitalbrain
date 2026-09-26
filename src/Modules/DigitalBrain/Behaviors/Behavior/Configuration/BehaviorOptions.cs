namespace DigitalBrain.Behavior;

public sealed class BehaviorOptions
{
    public const string SectionName = "DigitalBrain:Behavior";
    public string? Root { get; set; }
    public string? CodeRoot { get; set; }
    public string? Gateways { get; set; }
    public string? ClusterId { get; set; }
    public string? ServiceId { get; set; }
    public string DotnetPath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");
    public string? SandboxImage { get; set; }
    public string DockerPath { get; set; } = "docker";
    public TimeSpan StartupTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan StopTimeout { get; set; } = TimeSpan.FromSeconds(15);
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan HeartbeatLossTimeout { get; set; } = TimeSpan.FromSeconds(20);
    public long MaximumLogBytes { get; set; } = 10 * 1024 * 1024;
}