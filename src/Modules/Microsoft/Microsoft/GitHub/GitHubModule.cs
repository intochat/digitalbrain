using DigitalBrain.Core;
using DigitalBrain.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Hosting;

namespace DigitalBrain.Microsoft.GitHub;

internal static class GitHubModule
{
    internal static void Configure(ISiloBuilder builder)
    {
        builder.Services.AddOptions<GitHubRepositoriesOptions>()
            .Bind(builder.Configuration.GetSection(GitHubRepositoriesOptions.SectionName))
            .Validate(options => { _ = options.CreateBindings(); return true; })
            .ValidateOnStart();
        builder.Services.AddSingleton(services =>
            services.GetRequiredService<IOptions<GitHubRepositoriesOptions>>().Value.CreateBindings());
        builder.Services.AddGitHubAuthentication(builder.Configuration);
        builder.Services.AddSingleton<GitHubInstallationTokens>();
        builder.Services.AddSingleton<IGitHubRepositorySource, GitHubRepositorySource>();
        builder.Services.AddSingleton<GitHubSetupService>();
        builder.Services.AddSingleton<GitHubWebhookIngress>();
        builder.Services.AddSingleton<IHttpSurface, GitHubWebhookSurface>();
    }
}