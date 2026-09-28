namespace DigitalBrain.Microsoft.GitHub;

public sealed class GitHubAppHostingOptions
{
    public long AppId { get; set; }
    public string Slug { get; set; } = "";
    public string ClientId { get; set; } = "";
    public Uri? PublicOrigin { get; set; }
    public Uri? PublicWebhookUrl { get; set; }
}