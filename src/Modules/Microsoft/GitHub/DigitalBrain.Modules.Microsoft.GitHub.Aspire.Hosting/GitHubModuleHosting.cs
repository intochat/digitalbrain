using DigitalBrain.Aspire.Hosting;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Microsoft.GitHub;

public sealed class GitHubModuleHosting : IDigitalBrainModuleHosting
{
    public string Id => "github";
    public void Configure(DigitalBrainBuilder brain)
    {
        var repositories = brain.GetModuleConfiguration("github").GetModuleOptions<GitHubModuleOptions>("github").Repositories;
        var module = new DigitalBrainModuleBuilder<DigitalBrain.Microsoft.GitHub.GitHubModuleHosting>(brain);
        foreach (var (id, repo) in repositories)
        {
            module.WithGitHubRepository(id, repo.AppId, repo.InstallationId, repo.RepositoryId,
                repo.RepoOwner, repo.RepoName, repo.EndpointId, repo.ApiHost, repo.McpEndpoint);
        }
    }
}
