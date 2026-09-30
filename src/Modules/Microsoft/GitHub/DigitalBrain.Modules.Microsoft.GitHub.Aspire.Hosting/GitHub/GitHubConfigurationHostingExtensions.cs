using DigitalBrain.Aspire.Hosting;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Microsoft.GitHub;

public static class GitHubConfigurationHostingExtensions
{
    public static DigitalBrainModuleBuilder<GitHubModule> WithConfiguredGitHubApp(
        this DigitalBrainModuleBuilder<GitHubModule> module, IConfiguration configuration)
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
