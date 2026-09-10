using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Neurons;
using Orleans.Concurrency;

namespace DigitalBrain.Microsoft.GitHub;

[Alias("github.repository")]
public interface IRepository : INeuron
{
    /// <summary>Connects a validated repository.</summary>
    [Alias("connect")]
    Task<Accepted<RepositoryView>> Connect(ConnectRepository command);

    /// <summary>Schedules a repository refresh.</summary>
    [Alias("refresh")]
    Task<Accepted<RepositoryView>> Refresh(RefreshRepository command);

    /// <summary>Reads the repository snapshot.</summary>
    [ReadOnly]
    [Alias("read")]
    Task<RepositoryView> Read();

    /// <summary>Resolves repository readiness.</summary>
    [ReadOnly]
    [Alias("resolve")]
    Task<GitHubSetupResult> ResolveRepository(ResolveRepository query, CancellationToken cancellationToken = default);

    /// <summary>Reads current pull request evidence.</summary>
    [ReadOnly]
    [Alias("pull-request")]
    Task<PullRequestRead> ReadPullRequest(ReadPullRequest query, CancellationToken cancellationToken = default);

    /// <summary>Reads stored pull requests.</summary>
    [ReadOnly]
    [Alias("pull-requests")]
    Task<PullRequestsRead> ReadPullRequests();

    /// <summary>Reads stable complete review evidence.</summary>
    [ReadOnly]
    [Alias("review-evidence")]
    Task<ReviewEvidenceRead> ReadReviewEvidence(ReadReviewEvidence query, CancellationToken cancellationToken = default);

    /// <summary>Reads required CI checks.</summary>
    [ReadOnly]
    [Alias("required-checks")]
    Task<RequiredChecksRead> ReadRequiredChecks(CancellationToken cancellationToken = default);
}
