using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Neurons;
using Orleans.Concurrency;

namespace DigitalBrain.Microsoft.GitHub;

[Alias("github.repository")]
public interface IRepository : INeuron
{
    [Alias("connect")]
    Task<Accepted<RepositoryView>> Connect(ConnectRepository command);

    [Alias("refresh")]
    Task<Accepted<RepositoryView>> Refresh(RefreshRepository command);

    [ReadOnly]
    [Alias("read")]
    Task<RepositoryView> Read();

    [ReadOnly]
    [Alias("resolve")]
    Task<GitHubSetupResult> ResolveRepository(ResolveRepository query, CancellationToken cancellationToken = default);

    [ReadOnly]
    [Alias("pull-request")]
    Task<PullRequestRead> ReadPullRequest(ReadPullRequest query, CancellationToken cancellationToken = default);

    [ReadOnly]
    [Alias("pull-requests")]
    Task<PullRequestsRead> ReadPullRequests();

    [ReadOnly]
    [Alias("review-evidence")]
    Task<ReviewEvidenceRead> ReadReviewEvidence(ReadReviewEvidence query, CancellationToken cancellationToken = default);

    [ReadOnly]
    [Alias("required-checks")]
    Task<RequiredChecksRead> ReadRequiredChecks(CancellationToken cancellationToken = default);
}
