using DigitalBrain.Kernel;
using DigitalBrain.Platform.Contracts.Integrations;
using DigitalBrain.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Hosting;

namespace DigitalBrain.Microsoft.GitHub;

[ModuleId("github")]
public sealed class GitHubModule : IModule<GitHubModuleOptions>
{
    public static IntegrationDefinition Integration { get; } = IntegrationDefinition.For("github", "GitHub")
        .RequiresSecret("PrivateKeyPem")
        .RequiresSetting("AppId");

    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddOptions<GitHubRepositoriesOptions>()
            .Bind(builder.Configuration.GetSection(GitHubRepositoriesOptions.SectionName))
            .Validate(options => { _ = options.CreateBindings(); return true; })
            .ValidateOnStart();
        builder.Services.AddSingleton(services =>
            services.GetRequiredService<IOptions<GitHubRepositoriesOptions>>().Value.CreateBindings());
        builder.Services.AddGitHubAuthentication(builder.Configuration);
        builder.Services.AddSingleton<GitHubAppRegistration>();
        builder.Services.AddSingleton<GitHubInstallationTokens>();
        builder.Services.AddSingleton<IGitHubRepositorySource, GitHubRepositorySource>();
        builder.Services.AddSingleton<GitHubSetupService>();
        builder.Services.AddSingleton<GitHubWebhookIngress>();
        builder.Services.AddSingleton<IHttpSurface, GitHubWebhookSurface>();
    }
}
