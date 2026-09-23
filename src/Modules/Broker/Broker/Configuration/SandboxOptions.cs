namespace DigitalBrain.Broker;

// Process apps are off in the product profile until the G-8 sandbox penetration test closes. A
// hosted deployment turns them on only by setting DigitalBrain:Broker:Sandbox:Enabled=true after
// the test report is on file.
public sealed class SandboxOptions
{
    public const string SectionName = "DigitalBrain:Broker:Sandbox";

    public bool Enabled { get; set; }

    public double CpuCount { get; set; } = 0.5;

    public long MemoryBytes { get; set; } = 256L * 1024 * 1024;

    public int ProcessCount { get; set; } = 16;

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    public Dictionary<string, string> TrustedPublisherKeys { get; set; } = new(StringComparer.Ordinal);
}