using System.ComponentModel;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Core;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Microsoft.GitHub;

public sealed class GitHubNativeTools(IGrainFactory grains, BrowserLogins logins)
{
    internal AIFunction CreateConnectGitHubRepository()
    {
        Task<Accepted<RepositoryView>> Invoke(
            [Description("Repository binding identifier")] string bindingId,
            [Description("GitHub App identifier")] long appId,
            [Description("GitHub App installation identifier")] long installationId,
            [Description("GitHub repository identifier")] long repositoryId,
            [Description("Repository owner login")] string repositoryOwner,
            [Description("Repository name")] string repositoryName)
            => ConnectGitHubRepository(bindingId, new(CommandId.New(), appId, installationId, repositoryId, repositoryOwner, repositoryName));

        return AIFunctionFactory.Create(Invoke, new AIFunctionFactoryOptions
        {
            Name = "github_connect_repository",
            Description = "Connect a GitHub repository binding using its app, installation, and repository identifiers.",
        });
    }

    internal AIFunction CreateReadPullRequest()
    {
        Task<PullRequestRead> Invoke(
            [Description("Repository binding identifier")] string bindingId,
            [Description("Pull request number")] int number,
            [Description("Cancels the operation")] CancellationToken cancellationToken)
            => ReadPullRequest(bindingId, new(number), cancellationToken);

        return AIFunctionFactory.Create(Invoke, new AIFunctionFactoryOptions
        {
            Name = "github_pull_request",
            Description = "Read a GitHub pull request and its current snapshot for review.",
        });
    }

    internal AIFunction CreateReadPullRequests()
    {
        Task<PullRequestsRead> Invoke(
            [Description("Repository binding identifier")] string bindingId)
            => ReadPullRequests(bindingId);

        return AIFunctionFactory.Create(Invoke, new AIFunctionFactoryOptions
        {
            Name = "github_pull_requests",
            Description = "List pull requests in a connected GitHub repository.",
        });
    }

    internal AIFunction CreateReadReviewEvidence()
    {
        Task<ReviewEvidenceRead> Invoke(
            [Description("Repository binding identifier")] string bindingId,
            [Description("Expected snapshot returned by github_pull_request")] PullRequestSnapshot expected,
            [Description("Cancels the operation")] CancellationToken cancellationToken)
            => ReadReviewEvidence(bindingId, new(expected), cancellationToken);

        return AIFunctionFactory.Create(Invoke, new AIFunctionFactoryOptions
        {
            Name = "github_review_evidence",
            Description = "Read GitHub review evidence for an expected pull request snapshot.",
        });
    }

    internal AIFunction CreateReadRequiredChecks()
    {
        Task<RequiredChecksRead> Invoke(
            [Description("Repository binding identifier")] string bindingId,
            [Description("Cancels the operation")] CancellationToken cancellationToken)
            => ReadRequiredChecks(bindingId, cancellationToken);

        return AIFunctionFactory.Create(Invoke, new AIFunctionFactoryOptions
        {
            Name = "github_required_checks",
            Description = "Read the checks required for a connected GitHub repository.",
        });
    }

    internal AIFunction CreateResolveRepository()
    {
        Task<GitHubRepositoryStatus> Invoke(
            [Description("Repository binding identifier")] string bindingId,
            [Description("HTTPS GitHub repository URL")] string repositoryUrl,
            [Description("Cancels the operation")] CancellationToken cancellationToken)
            => ResolveRepository(bindingId, new(repositoryUrl), cancellationToken);

        return AIFunctionFactory.Create(Invoke, new AIFunctionFactoryOptions
        {
            Name = "github_resolve_repository",
            Description = "Resolve a GitHub repository URL and obtain a sign-in link when access requires authentication.",
        });
    }

    public Task<Accepted<RepositoryView>> ConnectGitHubRepository(string bindingId, ConnectRepository command)
        => Repository(bindingId).Connect(command);

    public Task<PullRequestRead> ReadPullRequest(string bindingId, ReadPullRequest query, CancellationToken cancellationToken = default)
        => Repository(bindingId).ReadPullRequest(query, cancellationToken);

    public Task<PullRequestsRead> ReadPullRequests(string bindingId) => Repository(bindingId).ReadPullRequests();

    public Task<ReviewEvidenceRead> ReadReviewEvidence(string bindingId, ReadReviewEvidence query, CancellationToken cancellationToken = default)
        => Repository(bindingId).ReadReviewEvidence(query, cancellationToken);

    public Task<RequiredChecksRead> ReadRequiredChecks(string bindingId, CancellationToken cancellationToken = default)
        => Repository(bindingId).ReadRequiredChecks(cancellationToken);

    public async Task<GitHubRepositoryStatus> ResolveRepository(string bindingId, ResolveRepository query, CancellationToken cancellationToken = default)
    {
        var result = await Repository(bindingId).ResolveRepository(query, cancellationToken).ConfigureAwait(false);
        return new(result, result.State is "authentication_required" or "access_revoked" ? logins.Require(result.RepositoryUrl) : null);
    }

    private IRepository Repository(string bindingId) => grains.GetGrain<IRepository>(new NeuronId("repository", bindingId).ToGrainId());
}
