using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Abstractions.Neurons;
using Orleans.Concurrency;

namespace DigitalBrain.Microsoft.GitHub;

[Alias("github.repository")]
public interface IRepository : INeuron
{
    [Alias("connect")]
    [NeuronTool]
    Task<Accepted<RepositoryView>> Connect(ConnectRepository command);

    [Alias("refresh")]
    [NeuronTool]
    Task<Accepted<RepositoryView>> Refresh(RefreshRepository command);

    [ReadOnly]
    [Alias("read")]
    [NeuronTool(IsReadOnly = true)]
    Task<RepositoryView> Read();

    [ReadOnly]
    [Alias("resolve")]
    [NeuronTool(IsReadOnly = true)]
    Task<GitHubSetupResult> ResolveRepository(ResolveRepository query, CancellationToken cancellationToken = default);

    [ReadOnly]
    [Alias("pull-request")]
    [NeuronTool(IsReadOnly = true)]
    Task<PullRequestRead> ReadPullRequest(ReadPullRequest query, CancellationToken cancellationToken = default);

    [ReadOnly]
    [Alias("pull-requests")]
    [NeuronTool(IsReadOnly = true)]
    Task<PullRequestsRead> ReadPullRequests();

    [ReadOnly]
    [Alias("review-evidence")]
    [NeuronTool(IsReadOnly = true)]
    Task<ReviewEvidenceRead> ReadReviewEvidence(ReadReviewEvidence query, CancellationToken cancellationToken = default);

    [ReadOnly]
    [Alias("required-checks")]
    [NeuronTool(IsReadOnly = true)]
    Task<RequiredChecksRead> ReadRequiredChecks(CancellationToken cancellationToken = default);
}
