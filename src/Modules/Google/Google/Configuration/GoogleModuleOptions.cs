namespace DigitalBrain.Google;

/// <summary>Public module settings. Credentials remain in the host's secret configuration.</summary>
public sealed record GoogleModuleOptions
{
    public Uri? PublicOrigin { get; set; }
    public Uri TokenEndpoint { get; set; } = new("https://oauth2.googleapis.com/token");
    public bool HostGmail { get; set; }
}