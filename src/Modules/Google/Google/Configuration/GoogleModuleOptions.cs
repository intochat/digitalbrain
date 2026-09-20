namespace DigitalBrain.Google;

/// <summary>Public module settings. Credentials remain in the host's secret configuration.</summary>
public sealed record GoogleModuleOptions
{
    public Uri? PublicOrigin { get; init; }
    public Uri TokenEndpoint { get; init; } = new("https://oauth2.googleapis.com/token");
}
