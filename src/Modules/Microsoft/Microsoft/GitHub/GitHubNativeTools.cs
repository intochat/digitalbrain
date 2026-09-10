using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Core;

namespace DigitalBrain.Microsoft.GitHub;

public sealed class GitHubNativeTools(IGrainFactory grains, BrowserLogins logins)
{
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
