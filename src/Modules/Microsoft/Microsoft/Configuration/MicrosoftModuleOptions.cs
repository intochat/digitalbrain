namespace DigitalBrain.Microsoft;

/// <summary>Public, non-secret module settings. Credentials stay in the host's secret configuration.</summary>
public sealed record MicrosoftModuleOptions
{
    public string? AspireProjectPath { get; set; }

    public string AspireApplicationName { get; set; } = "DigitalBrain";
    public Dictionary<string, GitHubRepositoryDeclaration> Repositories { get; set; } = new(StringComparer.Ordinal);
}

public sealed class GitHubRepositoryDeclaration
{
    public long AppId { get; set; }
    public long InstallationId { get; set; }
    public long RepositoryId { get; set; }
    public string RepoOwner { get; set; } = "";
    public string RepoName { get; set; } = "";
    public string? EndpointId { get; set; }
    public Uri? ApiHost { get; set; }
    public Uri? McpEndpoint { get; set; }
}
