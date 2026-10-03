using DigitalBrain.Contracts;

namespace DigitalBrain.Microsoft.GitHub;

// The declared repositories reach the AppHost's hosting adapter, which projects the repository identity keys and
// the private key and webhook secret parameters on the DigitalBrain:Microsoft:GitHub:Repositories configuration path.
public sealed class GitHubModuleOptions : IModuleOptions
{
    public Dictionary<string, GitHubRepositoryDeclaration> Repositories { get; set; } = new(StringComparer.Ordinal);

    public GitHubModuleOptions WithGitHubRepositories(IReadOnlyDictionary<string, GitHubRepositoryDeclaration> repositories)
    {
        Repositories = new(repositories, StringComparer.Ordinal);
        return this;
    }

    public void Validate()
    {
        foreach (var id in Repositories.Keys)
        {
            if (id.Length is 0 or > 80 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            { throw new ArgumentException("Repository binding IDs must contain letters, numbers or hyphens."); }
        }
    }
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
