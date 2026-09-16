using DigitalBrain.Aspire.Hosting;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Microsoft.Hosting;

public static class GitHubConfigurationHostingExtensions
{
    public static DigitalBrainModuleBuilder<MicrosoftModule> WithConfiguredGitHubRepositories(
        this DigitalBrainModuleBuilder<MicrosoftModule> module, IConfiguration configuration)
    {
        module.WithConfiguredGitHubApp(configuration);
        var repositories = configuration.GetSection("DigitalBrain:Microsoft:GitHub:Repositories")
            .Get<Dictionary<string, GitHubRepositoryHostingOptions>>() ?? [];
        foreach (var (id, options) in repositories)
        {
            module.WithGitHubRepository(id, options.AppId, options.InstallationId, options.RepositoryId,
                options.RepoOwner, options.RepoName, options.EndpointId, options.ApiHost, options.McpEndpoint);
        }
        return module;
    }

    public static DigitalBrainModuleBuilder<MicrosoftModule> WithConfiguredGitHubApp(
        this DigitalBrainModuleBuilder<MicrosoftModule> module, IConfiguration configuration)
    {
        var options = configuration.GetSection("DigitalBrain:Microsoft:GitHub:App").Get<GitHubAppHostingOptions>();
        if (options is not null)
        {
            ArgumentNullException.ThrowIfNull(options.PublicOrigin);
            ArgumentNullException.ThrowIfNull(options.PublicWebhookUrl);
            module.WithGitHubApp(options.AppId, options.Slug, options.ClientId,
                options.PublicOrigin, options.PublicWebhookUrl);
        }
        return module;
    }
}
