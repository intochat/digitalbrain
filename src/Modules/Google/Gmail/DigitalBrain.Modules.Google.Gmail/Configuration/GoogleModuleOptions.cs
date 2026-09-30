using DigitalBrain.Core;

namespace DigitalBrain.Google.Gmail;

/// <summary>Public module settings. Credentials remain in the host's secret configuration.</summary>
public sealed record GmailModuleOptions : IModuleOptions
{
    public Uri? PublicOrigin { get; set; }
    public Uri TokenEndpoint { get; set; } = new("https://oauth2.googleapis.com/token");
    public bool HostGmail { get; set; }

    public GmailModuleOptions WithGmail() { HostGmail = true; return this; }
    public GmailModuleOptions WithTokenEndpoint(Uri endpoint) { TokenEndpoint = endpoint; return this; }

    public void Validate()
    {
        if (PublicOrigin is { IsAbsoluteUri: false }) { throw new ArgumentException("Google PublicOrigin must be absolute."); }
        if (!TokenEndpoint.IsAbsoluteUri || TokenEndpoint.Scheme is not ("http" or "https"))
        { throw new ArgumentException("Google token endpoint must be an absolute HTTP URL."); }
    }
}
