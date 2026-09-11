namespace DigitalBrain.Microsoft.GitHub;

internal sealed class FakeGitHubRepositorySource : IGitHubRepositorySource
{
    internal const string WebhookSecret = "digitalbrain-fake-github-webhook-secret";
    internal static GitHubRepositoryBinding CreateBinding() => new("fake", 1, 2, 3, "fixture", "repository", "fake-private-key", WebhookSecret);

    public Task<PullRequestSnapshot> GetPullRequestAsync(GitHubRepositoryBinding binding, int number, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        binding.RequireEnabled();
        ArgumentOutOfRangeException.ThrowIfNotEqual(number, 42);
        var head = new string('a', 40);
        var basis = new string('b', 40);
        var at = new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero);
        return Task.FromResult(new PullRequestSnapshot(42, "Fake pull request", "https://github.com/fixture/repository/pull/42",
            true, false, head, basis, null, head,
            [new GitHubCheck("build", 3, "check", "completed", "success", head, "1", at)],
            true, at, at, "fake-revision", "fake-ci-revision", binding.RepositoryId, "main"));
    }

    public async Task<IReadOnlyList<PullRequestSnapshot>> ListOpenPullRequestsAsync(GitHubRepositoryBinding binding, CancellationToken cancellationToken)
        => [await GetPullRequestAsync(binding, 42, cancellationToken).ConfigureAwait(false)];

    public Task<GitHubReviewEvidence> GetReviewEvidenceAsync(GitHubRepositoryBinding binding, PullRequestSnapshot snapshot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        binding.RequireEnabled();
        const string text = "Fake review evidence";
        return Task.FromResult(new GitHubReviewEvidence(snapshot.HeadSha, snapshot.BaseSha, text, GitHubRepositorySource.Hash(text), true));
    }

    public Task<RequiredChecksRead> GetRequiredChecksAsync(GitHubRepositoryBinding binding, string? branch, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        binding.RequireEnabled();
        return Task.FromResult(new RequiredChecksRead([new GitHubCheckRequirement("build", 3)], true));
    }
}
