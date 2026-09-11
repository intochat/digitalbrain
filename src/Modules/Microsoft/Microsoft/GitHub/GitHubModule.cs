using DigitalBrain.AI;
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
        builder.Services.AddNativeTool("github_connect_repository", services => services.GetRequiredService<GitHubNativeTools>().CreateConnectGitHubRepository());
        builder.Services.AddNativeTool("github_pull_request", services => services.GetRequiredService<GitHubNativeTools>().CreateReadPullRequest());
        builder.Services.AddNativeTool("github_pull_requests", services => services.GetRequiredService<GitHubNativeTools>().CreateReadPullRequests());
        builder.Services.AddNativeTool("github_review_evidence", services => services.GetRequiredService<GitHubNativeTools>().CreateReadReviewEvidence());
        builder.Services.AddNativeTool("github_required_checks", services => services.GetRequiredService<GitHubNativeTools>().CreateReadRequiredChecks());
        builder.Services.AddNativeTool("github_resolve_repository", services => services.GetRequiredService<GitHubNativeTools>().CreateResolveRepository());
        builder.Services.AddSingleton(static services => new GitHubNativeTools(
            services.GetRequiredService<IGrainFactory>(), services.GetRequiredService<GitHubLogins>()));
    }
}
