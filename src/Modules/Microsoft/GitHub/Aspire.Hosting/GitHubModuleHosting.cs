using DigitalBrain.Aspire.Hosting;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Microsoft.GitHub;

public sealed class GitHubModuleHosting : IDigitalBrainModuleHosting
{
    public void Configure(DigitalBrainBuilder brain)
    {
        var repositories = brain.GetModuleConfiguration<GitHubModule>().GetSection("DigitalBrain:Microsoft:GitHub:Repositories")
            .Get<Dictionary<string, GitHubRepositoryDeclaration>>() ?? [];
        var module = new DigitalBrainModuleBuilder<GitHubModule>(brain);
        foreach (var (id, repo) in repositories)
        {
            module.WithGitHubRepository(id, repo.AppId, repo.InstallationId, repo.RepositoryId,
                repo.RepoOwner, repo.RepoName, repo.EndpointId, repo.ApiHost, repo.McpEndpoint);
        }
    }
}