namespace DigitalBrain.Microsoft.GitHub;

internal interface IGitHubRepositorySource
{
    Task<PullRequestSnapshot> GetPullRequestAsync(GitHubRepositoryBinding binding, int number, CancellationToken cancellationToken);
    Task<IReadOnlyList<PullRequestSnapshot>> ListOpenPullRequestsAsync(GitHubRepositoryBinding binding, CancellationToken cancellationToken);
    Task<GitHubReviewEvidence> GetReviewEvidenceAsync(GitHubRepositoryBinding binding, PullRequestSnapshot snapshot, CancellationToken cancellationToken);
    Task<RequiredChecksRead> GetRequiredChecksAsync(GitHubRepositoryBinding binding, string? branch, CancellationToken cancellationToken) => Task.FromResult(new RequiredChecksRead([], false, "Required CI checks have not been discovered for this source."));
}
