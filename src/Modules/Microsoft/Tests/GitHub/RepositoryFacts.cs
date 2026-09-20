using DigitalBrain.Microsoft;
using DigitalBrain.Microsoft.GitHub;
using DigitalBrain.Microsoft.GitHub.Signals;
using DigitalBrain.Testing.Unit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class RepositoryFacts
{
    [Fact]
    public async Task ConnectPublishesRepositoryConnectedAndReadsBack()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(new FakeGitHubRepositorySource(), ct);
        var repository = brain.Get<IRepository>("repo");
        await using var connected = await brain.Observe<RepositoryConnected>(repository, ct);
        var view = await repository.Connect(new(7, 9, 11, "intochat", "digitalbrain"));
        Assert.True(view.Connected);
        var published = await connected.NextAsync(ct: ct);
        Assert.Equal("repo", published.BindingId);
        Assert.Equal(11, published.RepositoryId);
    }

    [Fact]
    public async Task RepositoryEventPublishesPullRequestChangedAndDeduplicates()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(new FakeGitHubRepositorySource(), ct);
        var repository = brain.Get<IRepository>("repo");
        await repository.Connect(new(7, 9, 11, "intochat", "digitalbrain"));
        await using var changed = await brain.Observe<PullRequestChanged>(repository, ct);
        var accepted = await repository.AcceptRepositoryEvent(new("delivery-1", "pull_request", "opened", 1, "hash"), ct);
        Assert.True(accepted);
        Assert.Equal(1, (await changed.NextAsync(ct: ct)).Number);
        Assert.False(await repository.AcceptRepositoryEvent(new("delivery-1", "pull_request", "opened", 1, "hash"), ct));
    }

    [Fact]
    public async Task RefreshReadsAvailablePullRequests()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(new FakeGitHubRepositorySource(), ct);
        var repository = brain.Get<IRepository>("repo");
        await repository.Connect(new(7, 9, 11, "intochat", "digitalbrain"));
        var view = await repository.Refresh(new());
        Assert.True(view.Connected);
        Assert.Equal("repo", view.BindingId);
        Assert.Equal(1, Assert.Single(view.PullRequests).Number);
    }

    [Fact]
    public async Task ConnectWithMismatchedCoordinatesIsRefusedWithoutConnecting()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(new FakeGitHubRepositorySource(), ct);
        var repository = brain.Get<IRepository>("repo");
        await using var refused = await brain.Observe<RepositoryRefused>(repository, ct);
        var view = await repository.Connect(new(7, 9, 99, "intochat", "digitalbrain"));
        Assert.False(view.Connected);
        Assert.Equal("The connection does not match its authorized binding.", (await refused.NextAsync(ct: ct)).Reason);
    }

    private static Task<UnitBrain> StartAsync(IGitHubRepositorySource source, CancellationToken ct)
        => DigitalBrainSimulation.StartAsync(new()
        {
            Modules = [new MicrosoftModule()],
            ConfigureSilo = silo =>
            {
                silo.Services.AddSingleton(new GitHubRepositoryBindings([Binding()]));
                silo.Services.AddSingleton(source);
            },
        }, ct);

    private static GitHubRepositoryBinding Binding()
        => new("repo", 11, 9, 7, "intochat", "digitalbrain", "private-key", "0123456789abcdef");
}

internal sealed class FakeGitHubRepositorySource : IGitHubRepositorySource
{
    public Task<PullRequestSnapshot> GetPullRequestAsync(GitHubRepositoryBinding binding, int number, CancellationToken cancellationToken)
        => Task.FromResult(Snapshot(binding, number));

    public Task<IReadOnlyList<PullRequestSnapshot>> ListOpenPullRequestsAsync(GitHubRepositoryBinding binding, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<PullRequestSnapshot>>([Snapshot(binding, 1)]);

    public Task<GitHubReviewEvidence> GetReviewEvidenceAsync(GitHubRepositoryBinding binding, PullRequestSnapshot snapshot, CancellationToken cancellationToken)
        => Task.FromResult(new GitHubReviewEvidence(snapshot.HeadSha, snapshot.BaseSha, "evidence", "hash", true));

    private static PullRequestSnapshot Snapshot(GitHubRepositoryBinding binding, int number) => new(
        number, "Title", $"https://github.com/{binding.RepoOwner}/{binding.RepoName}/pull/{number}", true, false,
        new string('a', 40), new string('b', 40), null, new string('a', 40), [], true,
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "revision-1", "ci-revision-1", binding.RepositoryId, "main");
}