namespace DigitalBrain.Microsoft.GitHub;

// Contains credentials. Do not serialize or log this options instance.
public sealed class GitHubAppOptions
{
    public const string SectionName = "DigitalBrain:Microsoft:GitHub:App";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string AppId { get; set; } = "";
    public string Slug { get; set; } = "";
    public string PublicOrigin { get; set; } = "";
    public string PublicWebhookUrl { get; set; } = "";
    public string PrivateKeyPem { get; set; } = "";
    public string WebhookSecret { get; set; } = "";
    internal long ParsedAppId => long.TryParse(AppId, out var id) ? id : 0;
}
