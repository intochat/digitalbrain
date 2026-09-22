namespace DigitalBrain.Google.Gmail;

// Contains credentials. Do not serialize or log this options instance.
public sealed class GmailOAuthOptions
{
    public const string SectionName = "DigitalBrain:Google:Gmail:OAuth";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string PublicOrigin { get; set; } = "";
    public string TokenEndpoint { get; set; } = "";
}