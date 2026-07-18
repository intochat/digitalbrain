using Ino.Core;
using Ino.Core.Hosting;
using Ino.Kernel;
using Ino.Kernel.Contracts;
using Ino.Testing;
using Orleans;
using Xunit;

namespace Ino.Kernel.Tests;

/// <summary>
/// Phase 4 Slice E.1: <see cref="IMissedIntentTracker"/> counts near-duplicate
/// <see cref="UnroutedIntent"/>s per user and emits an
/// <see cref="L1Proposal"/> broadcast at the cluster threshold (3 v0.1).
/// Tests run against the in-memory test silo so the tracker journals in the
/// same way it will in production.
/// </summary>
[Collection(nameof(InoTestCollection))]
public sealed class MissedIntentTrackerTests
{
    private readonly InoTestSiloFixture _fixture;

    public MissedIntentTrackerTests(InoTestSiloFixture fixture)
    {
        _fixture = fixture;
    }

    static string UniqueUser() => $"user-{Guid.NewGuid():n}";

    [Fact]
    public async Task Records_unrouted_to_user_journal()
    {
        var userId = UniqueUser();
        var tracker = _fixture.Grains.GetGrain<IMissedIntentTracker>(userId);

        await tracker.RecordAsync("remind me to call mom in 5 minutes", Guid.NewGuid().ToString("n"));

        var history = await tracker.GetHistoryAsync(int.MaxValue);
        Assert.Single(history);
        Assert.Equal("remind me to call mom in 5 minutes", history[0].Text);
        Assert.Equal(userId, history[0].UserId);
    }

    [Fact]
    public async Task Three_identical_prompts_emit_a_single_L1Proposal()
    {
        var userId = UniqueUser();
        var tracker = _fixture.Grains.GetGrain<IMissedIntentTracker>(userId);

        for (var i = 0; i < 3; i++)
            await tracker.RecordAsync("remind me to call mom in 5 minutes", $"corr-{i}");

        var history = await tracker.GetHistoryAsync(int.MaxValue);
        // 3 unrouted records + 1 sentinel marker = 4 entries.
        Assert.Equal(4, history.Count);
        Assert.Equal(MissedIntentTracker.L1ProposalEmittedSentinel, history[3].UserId);

        // Further matching prompts are recorded but do NOT re-emit.
        await tracker.RecordAsync("remind me to call mom in 5 minutes", "corr-extra");
        var afterExtra = await tracker.GetHistoryAsync(int.MaxValue);
        Assert.Equal(5, afterExtra.Count);
        Assert.Equal(1, afterExtra.Count(p => p.UserId == MissedIntentTracker.L1ProposalEmittedSentinel));
    }

    [Theory]
    [InlineData("Remind me to call mom in 5 minutes", "remind me to call mom in 5 minutes")]
    [InlineData("  REMIND ME to CALL mom  in 5 minutes  ", "remind me to call mom in 5 minutes")]
    [InlineData("remind me to call mom in 5 minutes!", "remind me to call mom in 5 minutes")]
    public void NormalizeForCluster_handles_case_whitespace_and_trailing_punctuation(string input, string expected)
    {
        Assert.Equal(expected, MissedIntentTracker.NormalizeForCluster(input));
    }

    [Fact]
    public async Task Distinct_clusters_each_get_their_own_proposal()
    {
        var userId = UniqueUser();
        var tracker = _fixture.Grains.GetGrain<IMissedIntentTracker>(userId);

        for (var i = 0; i < 3; i++)
            await tracker.RecordAsync("remind me to call mom", $"corr-a-{i}");
        for (var i = 0; i < 3; i++)
            await tracker.RecordAsync("set timer for 30 minutes", $"corr-b-{i}");

        var history = await tracker.GetHistoryAsync(int.MaxValue);
        // 6 unrouted prompts + 2 sentinels = 8 entries.
        Assert.Equal(8, history.Count);
        Assert.Equal(2, history.Count(p => p.UserId == MissedIntentTracker.L1ProposalEmittedSentinel));
    }

    [Fact]
    public async Task Two_records_below_threshold_do_not_emit()
    {
        var userId = UniqueUser();
        var tracker = _fixture.Grains.GetGrain<IMissedIntentTracker>(userId);

        await tracker.RecordAsync("schedule something", "corr-1");
        await tracker.RecordAsync("schedule something", "corr-2");

        var history = await tracker.GetHistoryAsync(int.MaxValue);
        Assert.Equal(2, history.Count);
        Assert.DoesNotContain(history, p => p.UserId == MissedIntentTracker.L1ProposalEmittedSentinel);
    }
}
