using DigitalBrain.Contracts;

namespace DigitalBrain.Google.Gmail;

public sealed record GmailModuleOptions : IModuleOptions
{
    public Uri TokenEndpoint { get; set; } = new("https://oauth2.googleapis.com/token");
    public bool HostGmail { get; set; }

    public GmailModuleOptions WithGmail() { HostGmail = true; return this; }
    public GmailModuleOptions WithTokenEndpoint(Uri endpoint) { TokenEndpoint = endpoint; return this; }

    public void Validate()
    {
        if (!TokenEndpoint.IsAbsoluteUri || TokenEndpoint.Scheme is not ("http" or "https"))
        { throw new ArgumentException("Google token endpoint must be an absolute HTTP URL."); }
    }
}
