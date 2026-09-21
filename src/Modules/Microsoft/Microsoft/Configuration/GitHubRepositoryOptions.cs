namespace DigitalBrain.Microsoft.GitHub;

// Contains credentials. Do not serialize or log this options instance.
public sealed class GitHubRepositoryOptions
{
    public long RepositoryId { get; set; }
    public long InstallationId { get; set; }
    public long AppId { get; set; }
    public string RepoOwner { get; set; } = "";
    public string RepoName { get; set; } = "";
    public string PrivateKeyPem { get; set; } = "";
    public string WebhookSecret { get; set; } = "";
    public string? EndpointId { get; set; }
    public Uri? ApiHost { get; set; }
    public Uri? McpEndpoint { get; set; }

    internal GitHubRepositoryBinding CreateBinding(string id) => new(id, RepositoryId, InstallationId,
        AppId, RepoOwner, RepoName, PrivateKeyPem, WebhookSecret, EndpointId, ApiHost, McpEndpoint);
}

public sealed class GitHubRepositoriesOptions
{
    public const string SectionName = "DigitalBrain:Microsoft:GitHub";
    public Dictionary<string, GitHubRepositoryOptions> Repositories { get; set; } = new(StringComparer.Ordinal);

    internal GitHubRepositoryBindings CreateBindings() => new(Repositories.Select(pair => pair.Value.CreateBinding(pair.Key)));
}