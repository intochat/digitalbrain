using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.Microsoft.GitHub;

[Alias("github.repository")]
[Orleans.Metadata.DefaultGrainType("github.repository")]
public interface IRepository : INeuron
{
    Task<RepositoryView> Connect(ConnectRepository request);

    Task<RepositoryView> Refresh(RefreshRepository request);

    Task<bool> AcceptRepositoryEvent(RepositoryEvent receipt, CancellationToken cancellationToken = default);

    [ReadOnly]
    Task<RepositoryView> Read();

    [ReadOnly]
    Task<GitHubSetupResult> ResolveRepository(ResolveRepository query, CancellationToken cancellationToken = default);

    [ReadOnly]
    Task<PullRequestRead> ReadPullRequest(ReadPullRequest query, CancellationToken cancellationToken = default);

    [ReadOnly]
    Task<PullRequestsRead> ReadPullRequests();

    [ReadOnly]
    Task<ReviewEvidenceRead> ReadReviewEvidence(ReadReviewEvidence query, CancellationToken cancellationToken = default);

    [ReadOnly]
    Task<RequiredChecksRead> ReadRequiredChecks(CancellationToken cancellationToken = default);
}