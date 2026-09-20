namespace DigitalBrain.Google;

/// <summary>Public module settings. Credentials remain in the host's secret configuration.</summary>
public sealed record GoogleModuleOptions
{
    public Uri? PublicOrigin { get; init; }
}
