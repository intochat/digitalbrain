namespace DigitalBrain.Microsoft.GitHub;

public sealed class GitHubRepositoryHostingOptions
{
    public string BindingId { get; set; } = "";
    public long AppId { get; set; }
    public long InstallationId { get; set; }
    public long RepositoryId { get; set; }
    public string RepoOwner { get; set; } = "";
    public string RepoName { get; set; } = "";
    public string? EndpointId { get; set; }
    public Uri? ApiHost { get; set; }
    public Uri? McpEndpoint { get; set; }
}