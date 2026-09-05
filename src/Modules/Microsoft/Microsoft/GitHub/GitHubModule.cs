using DigitalBrain.AI;
using DigitalBrain.Core;
using DigitalBrain.Product.Presentation;
using DigitalBrain.Sdk;
using DigitalBrain.Sdk.Webhooks;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Microsoft.GitHub;

internal static class GitHubModule
{
    internal static void Configure(ISiloBuilder builder)
    {
        var bindings = GitHubRepositoryBindings.Read(builder.Configuration);
        builder.Services.AddSingleton(bindings);
        builder.Services.AddSingleton<GitHubInstallationTokens>();
        builder.Services.AddSingleton<IGitHubRepositorySource, GitHubRepositorySource>();
        builder.Services.AddSingleton<GitHubRepositoryConnections>();
        builder.Services.AddSingleton(new NeuronPresentation("repository", "GitHub Repository", "Microsoft", "github"));
        builder.Services.AddSingleton(new NeuronPresentation("github-dispatcher", "GitHub updates", "Microsoft", "github"));
        builder.Services.AddSingleton(new NeuronPresentation("pullrequestreview", "PR review", "Microsoft", "review"));
        builder.Services.AddSingleton(new NeuronPresentation("github-review-worker", "Review worker", "Microsoft", "review"));
        builder.Services.AddSingleton(new NeuronPresentation("architecturereviewer", "Architecture review", "Microsoft", "architecture"));
        builder.Services.AddSingleton(new NeuronPresentation("codequalityreviewer", "Code quality review", "Microsoft", "quality"));
        if (bindings.All.Count == 0)
        {
            return;
        }
        var delegationNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var binding in bindings.All)
        {
            binding.BeginRecovery();
            builder.Services.AddSingleton<IHttpSurface>(services => new WebhookSurface(
                new WebhookDefinition($"/integrations/github/{binding.EndpointId}/webhook"),
                new GitHubWebhookHandler(binding, services.GetRequiredService<IGrainFactory>())));
            var name = bindings.All.Count == 1 ? "ask_repository" : $"ask_repository_{binding.Id.Replace('-', '_').Replace('.', '_')}";
            if (!delegationNames.Add(name))
            {
                throw new InvalidOperationException("GitHub binding names must remain unique when '-' and '.' are normalized to '_'.");
            }
            builder.Services.AddSingleton<IAgentToolSource>(new GitHubRepositoryDelegation(binding, new AgentDelegation<IRepository>(name,
                $"Ask the GitHub repository specialist about {binding.RepoOwner}/{binding.RepoName}. "
                    + "Use for current pull requests, files and CI evidence. This is one configured, read-only repository. "
                    + $"Trusted script coordinates: bindingId={binding.Id}, repositoryId={binding.RepositoryId}, repositoryInstance={binding.InstanceName}. "
                    + "The repository instance is fixed to its configured automation principal. "
                    + "Pass the question and PR number; the specialist cannot post comments, modify files or merge.",
                binding.LocalName, binding.Owner)));
        }
        builder.Services.AddHostedService<GitHubWebhookDispatcher>();
    }
}
