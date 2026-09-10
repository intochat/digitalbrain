using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Microsoft.GitHub;

internal static class GitHubModule
{
    internal static void Configure(ISiloBuilder builder)
    {
        var fake = DigitalBrainFakes.Enabled(builder.Configuration);
        var bindings = fake ? new GitHubRepositoryBindings([FakeGitHubRepositorySource.CreateBinding()])
            : GitHubRepositoryBindings.Read(builder.Configuration);
        builder.Services.AddSingleton(bindings);
        builder.Services.AddGitHubAuthentication(builder.Configuration);
        builder.Services.AddSingleton<GitHubInstallationTokens>();
        if (fake)
        {
            builder.Services.AddSingleton<IGitHubRepositorySource, FakeGitHubRepositorySource>();
        }
        else
        {
            builder.Services.AddSingleton<IGitHubRepositorySource, GitHubRepositorySource>();
        }
        builder.Services.AddSingleton<GitHubSetupService>();
        builder.Services.AddSingleton<GitHubWebhookIngress>();
        builder.Services.AddSingleton<IHttpSurface, GitHubWebhookSurface>();
        // NativeTools contributor lands after the AI module merge
        builder.Services.AddSingleton(static services => new GitHubNativeTools(
            services.GetRequiredService<IGrainFactory>(), services.GetRequiredService<GitHubLogins>()));
    }
}
