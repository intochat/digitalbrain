namespace DigitalBrain.Google.Gmail;

/// <summary>Public module settings. Credentials remain in the host's secret configuration.</summary>
public sealed record GmailModuleOptions
{
    public Uri? PublicOrigin { get; set; }
    public Uri TokenEndpoint { get; set; } = new("https://oauth2.googleapis.com/token");
    public bool HostGmail { get; set; }
}