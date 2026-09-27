namespace DigitalBrain.Microsoft.CSharp;

public sealed class CSharpOptions
{
    public const string SectionName = "DigitalBrain:CSharp";

    public string? Root { get; set; }
    public string? SourceRoot { get; set; }
    public string Image { get; set; } = "mcr.microsoft.com/dotnet/sdk:11.0.100-rc.1";
    public string DockerPath { get; set; } = "docker";
    public string? Gateways { get; set; }
    public string? GatewayRelayHost { get; set; }
    public string? ClusterId { get; set; }
    public string? ServiceId { get; set; }
    public TimeSpan DockerTimeout { get; set; } = TimeSpan.FromMinutes(2);
}
