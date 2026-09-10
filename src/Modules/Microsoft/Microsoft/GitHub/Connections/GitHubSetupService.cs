using DigitalBrain.Abstractions.Identity;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Microsoft.GitHub;

internal sealed class GitHubSetupService(IConfiguration configuration, GitHubRepositoryBindings bindings, IGrainFactory grains, GitHubInstallationTokens tokens, IGitHubRepositorySource source)
{
    internal const string AppRoot = "DigitalBrain:Microsoft:GitHub:App";
    public async Task<GitHubSetupResult> ResolveAsync(string repositoryUrl, CancellationToken cancellationToken = default, RepositoryView? localView = null)
    {
        var coordinates = ParseUrl(repositoryUrl);
        var url = $"https://github.com/{coordinates.Owner}/{coordinates.Name}";
        var binding = bindings.FindByRepository(coordinates.Owner, coordinates.Name);
        if (binding is null)
        {
            var saved = (await grains.GetGrain<IGitHubConnections>(new NeuronId("github", "connections").ToGrainId())
                .List().WaitAsync(cancellationToken)).Connections.FirstOrDefault(record =>
                    string.Equals(record.RepositoryOwner, coordinates.Owner, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(record.RepositoryName, coordinates.Name, StringComparison.OrdinalIgnoreCase));
            if (saved is not null && Configured(saved.AppId))
            {
                binding = Create(saved);
            }
        }
        if (binding is null)
        {
            return new(url, Configured(null) ? "authentication_required" : "operator_setup_required", null, [], Configured(null) ? "Connect GitHub and select repository access to resume this request." : "The operator must configure the GitHub App id, private key, webhook secret and OAuth callback before connection.");
        }

        var id = binding.Id;
        var view = localView?.BindingId == id ? localView
            : await grains.GetGrain<IRepository>(new NeuronId("repository", id).ToGrainId()).Read().WaitAsync(cancellationToken);
        if (!binding.Enabled || view.Revoked)
        {
            return new(url, "access_revoked", id, [], "Reconnect repository access before enabling subscriptions.");
        }

        var ingress = configuration[$"{AppRoot}:PublicWebhookUrl"];
        if (!Uri.TryCreate(ingress, UriKind.Absolute, out var endpoint) || endpoint.Scheme != "https"
            || endpoint.AbsolutePath != "/integrations/github/webhook" || endpoint.UserInfo.Length != 0
            || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0)
        {
            return new(url, "webhook_setup_required", id, [], "Configure a public HTTPS URL for /integrations/github/webhook. Successful sign-in does not establish inbound reachability.");
        }

        var proof = view.LastWebhookAt;
        if (proof is null)
        {
            return new(url, "webhook_verification_required", id, [], "Repository access is connected. Send a GitHub App ping or redeliver an event to verify authenticated inbound delivery. Required CI checks are validated after delivery is verified.", ingress);
        }

        RequiredChecksRead checks;
        try
        {
            checks = await source.GetRequiredChecksAsync(binding, null, cancellationToken);
        }
        catch (Exception error) when (error is GitHubAccessDeniedException or GitHubUnavailableException)
        {
            return new(url, error is GitHubAccessDeniedException ? "access_denied" : "unavailable", id, [], "GitHub readiness could not be established. Verify repository access and try again.");
        }

        if (!checks.Complete || checks.Checks.Count == 0)
        {
            return new(url, "ci_setup_required", id, checks.Checks, checks.Detail ?? "Select an explicit nonempty set of required CI checks.", ingress);
        }

        return new(url, "ready", id, checks.Checks, "Repository access, required CI checks and authenticated webhook delivery are validated.", ingress);
    }

    public async Task<GitHubSetupResult> ConnectAsync(GitHubRepositoryAccess access, CancellationToken cancellationToken = default)
    {
        if (!Configured(access.AppId))
        {
            throw new GitHubUnavailableException("GitHub App operator setup is incomplete or this installation belongs to a different App.");
        }

        var prior = bindings.FindByRepository(access.RepositoryOwner, access.RepositoryName);
        var identity = prior?.Id ?? $"r-{access.RepositoryId}";
        var previous = await grains.GetGrain<IRepository>(new NeuronId("repository", identity).ToGrainId())
            .Read().WaitAsync(cancellationToken);
        var record = new GitHubConnectionRecord(identity, access.AppId, access.InstallationId, access.RepositoryId, access.RepositoryOwner, access.RepositoryName, prior is { Enabled: true } && !previous.Revoked && prior.InstallationId == access.InstallationId ? prior.Revision : Guid.NewGuid().ToString("N"));
        var binding = Create(record);
        // Installation-token exchange verifies the App can actually read this numeric repository.
        _ = await tokens.GetTokenAsync(binding, true, cancellationToken);
        await tokens.VerifyRepositoryAsync(binding, cancellationToken);
        await grains.GetGrain<IGitHubConnections>(new NeuronId("github", "connections").ToGrainId())
            .Register(new RegisterGitHubConnection(CommandId.New(), record.Id, record.AppId, record.InstallationId,
                record.RepositoryId, record.RepositoryOwner, record.RepositoryName, record.Epoch)).WaitAsync(cancellationToken);
        bindings.Add(binding);
        await grains.GetGrain<IRepository>(new NeuronId("repository", binding.Id).ToGrainId())
            .Connect(new ConnectRepository(CommandId.New(), binding.AppId, binding.InstallationId,
                binding.RepositoryId, binding.RepoOwner, binding.RepoName)).WaitAsync(cancellationToken);
        return await ResolveAsync($"https://github.com/{access.RepositoryOwner}/{access.RepositoryName}", cancellationToken);
    }

    private bool Configured(long? appId) => long.TryParse(configuration[$"{AppRoot}:AppId"], out var configured) && configured > 0 && (appId is null || appId == configured) && !string.IsNullOrWhiteSpace(configuration[$"{AppRoot}:PrivateKeyPem"]) && configuration[$"{AppRoot}:WebhookSecret"] is { Length: >= 16 };
    private GitHubRepositoryBinding Create(GitHubConnectionRecord record) => new(record.Id, record.RepositoryId, record.InstallationId, record.AppId, record.RepositoryOwner, record.RepositoryName, configuration[$"{AppRoot}:PrivateKeyPem"]!, configuration[$"{AppRoot}:WebhookSecret"]!, authorizationEpoch: record.Epoch);
    internal static (string Owner, string Name) ParseUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "github.com" || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
        {
            throw new GitHubUnavailableException("Use the HTTPS GitHub repository URL, for example https://github.com/intochat/digitalbrain.");
        }

        var parts = uri.AbsolutePath.Trim('/').Split('/');
        if (parts.Length != 2 || parts.Any(part => part.Length == 0 || part.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.')))
        {
            throw new GitHubUnavailableException("Use a repository URL with an owner and repository name.");
        }

        return (parts[0], parts[1].EndsWith(".git", StringComparison.Ordinal) ? parts[1][..^4] : parts[1]);
    }
}
