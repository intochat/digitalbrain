using System.Text.Json;
using DigitalBrain.Units;

namespace DigitalBrain.Tests.Unit.Synapses;

public sealed class AsksStateFacts
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Grace = AsksNeuron.Grace;

    private static Ask MakeAsk(string key, bool repeatable = false, TimeSpan? timeout = null) =>
        new("feed", "Get", JsonElement.Parse("""{"id":"1"}"""), key)
        {
            Repeatable = repeatable,
            Timeout = timeout,
        };

    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 2)]
    [InlineData(2, 10)]
    [InlineData(3, 10)]
    [InlineData(10, 10)]
    public void RetryPolicyNextClampsAndReturnsExpectedBackoff(int attempt, int expectedSeconds) =>
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), RetryPolicy.Next(attempt));

    [Fact]
    public void AcceptDeduplicatesKeysEnforcesBudgetAndRollsOverHour()
    {
        var openKeys = new HashSet<string>(["dup"], StringComparer.Ordinal);
        var asks = new[]
        {
            MakeAsk("dup"),
            MakeAsk("fresh-1", timeout: TimeSpan.FromSeconds(45)),
            MakeAsk("fresh-1"), // intra-batch duplicate
        };

        var accepted = new AsksState().Accept("b1", asks, openKeys, T0);

        Assert.True(accepted.Save);
        Assert.True(accepted.CreatedAnswers);
        Assert.Single(accepted.Accepted);
        Assert.Equal("fresh-1", accepted.Accepted[0].Key);
        Assert.Equal(2, accepted.State.Unsent.Count);
        Assert.All(accepted.State.Unsent, a =>
        {
            Assert.Equal(AnswerStatus.Refused, a.Status);
            Assert.Equal("already-open", a.Error);
        });
        var batch = Assert.Single(accepted.State.Batches);
        Assert.Equal(("b1", 1, TimeSpan.FromSeconds(45)), (batch.Id, batch.Count, batch.Timeout));

        // Exhaust hourly limit and confirm Trip + hour rollover recovery.
        var atLimit = new AsksState([], [], null, T0.ToUnixTimeSeconds() / 3600, AskLimits.MaxAsksPerHour, false);
        var tripped = atLimit.Accept("b2", [MakeAsk("k-over")], new HashSet<string>(StringComparer.Ordinal), T0);
        Assert.True(tripped.Save);
        Assert.Empty(tripped.Accepted);
        Assert.True(tripped.State.Tripped);
        Assert.Equal("over-budget", Assert.Single(tripped.State.Unsent).Error);

        var dropped = tripped.State.Accept("b3", [MakeAsk("k-dropped")], new HashSet<string>(StringComparer.Ordinal), T0);
        Assert.False(dropped.Save);
        Assert.Empty(dropped.Accepted);

        var nextHour = tripped.State.Accept("b4", [MakeAsk("k-next-hour")], new HashSet<string>(StringComparer.Ordinal), T0.AddHours(1));
        Assert.True(nextHour.Save);
        Assert.False(nextHour.State.Tripped);
        Assert.Equal(1, nextHour.State.AskedThisHour);
        Assert.Single(nextHour.Accepted);
    }

    [Fact]
    public void StartPrioritizesReadyRetriesRespectsConcurrencyAndSetsFlyingDue()
    {
        var batch = new OpenBatch(
            "b1",
            Count: 6,
            Next: 1,
            Flying: [new FlyingAsk(0, 1, T0.AddSeconds(35))],
            Waiting: [new WaitingAsk(10, 2, T0.AddSeconds(1)), new WaitingAsk(11, 2, T0.AddSeconds(5))],
            Settled: 0,
            Timeout: TimeSpan.FromSeconds(20),
            AskedAt: T0);
        var state = new AsksState([batch], [], null, T0.ToUnixTimeSeconds() / 3600, 6, false);

        var started = state.Start(T0.AddSeconds(2), Grace);

        Assert.Equal(AskLimits.Concurrency - 1, started.Started.Count);
        Assert.Equal([(10, 2), (1, 1), (2, 1)], started.Started.Select(s => (s.Index, s.Attempt)));
        var nextBatch = Assert.Single(started.State.Batches);
        Assert.Equal(AskLimits.Concurrency, nextBatch.Flying.Count);
        Assert.Single(nextBatch.Waiting); // index 11 still waiting for t0 + 5s
        Assert.All(nextBatch.Flying.Skip(1), f => Assert.Equal(T0.AddSeconds(2) + TimeSpan.FromSeconds(20) + Grace, f.Due));
    }

    [Fact]
    public void SettleSchedulesRetryWithRetryPolicyAndClosesCompletedBatch()
    {
        var initial = new AsksState()
            .Accept("b1", [MakeAsk("k0", repeatable: true)], new HashSet<string>(StringComparer.Ordinal), T0)
            .State
            .Start(T0, Grace)
            .State;

        var transientFail = new AskSettlement("b1", 0, 1, "feed", "Get", "k0", Repeatable: true, AnswerStatus.Failed, AskRules.NoResult, "down", Transient: true);
        var retried = initial.Settle(transientFail, T0.AddSeconds(1));

        Assert.True(retried.Changed);
        Assert.Null(retried.DroppedBatchId);
        Assert.Empty(retried.State.Unsent);
        var waiting = Assert.Single(Assert.Single(retried.State.Batches).Waiting);
        Assert.Equal((0, 2, T0.AddSeconds(1) + RetryPolicy.Next(1)), (waiting.Index, waiting.Attempt, waiting.NotBefore));

        var restarted = retried.State.Start(waiting.NotBefore, Grace).State;
        var ok = new AskSettlement("b1", 0, 2, "feed", "Get", "k0", Repeatable: true, AnswerStatus.Ok, JsonElement.Parse("""{"ok":true}"""), null, Transient: false);
        var closed = restarted.Settle(ok, waiting.NotBefore.AddSeconds(1));

        Assert.True(closed.Changed);
        Assert.Equal("b1", closed.DroppedBatchId);
        Assert.Empty(closed.State.Batches);
        Assert.Equal(AnswerStatus.Ok, Assert.Single(closed.State.Unsent).Status);
    }

    [Fact]
    public void ExtendLeaseDurablyUpdatesFlyingDueAndSweepHonorsIt()
    {
        var asks = new Dictionary<string, IReadOnlyList<Ask>>(StringComparer.Ordinal)
        {
            ["b1"] = [MakeAsk("k0", repeatable: false, timeout: TimeSpan.FromSeconds(10))],
        };

        var state = new AsksState()
            .Accept("b1", asks["b1"], new HashSet<string>(StringComparer.Ordinal), T0)
            .State
            .Start(T0, Grace)
            .State;

        var extended = state.ExtendLease("b1", 0, 1, TimeSpan.FromSeconds(20), T0.AddSeconds(8), Grace);
        Assert.True(extended.Matched);
        Assert.True(extended.Changed);
        Assert.Equal(T0.AddSeconds(8) + TimeSpan.FromSeconds(20) + Grace, extended.State.Batches[0].Flying[0].Due);

        // Past initial deadline (T0 + 15s) but before extended deadline (T0 + 33s): Sweep must NOT time out.
        var notYet = extended.State.Sweep(asks, T0.AddSeconds(16));
        Assert.False(notYet.Changed);
        Assert.Empty(notYet.State.Unsent);

        // Past extended deadline (T0 + 33s): Sweep settles Unknown/"timeout" and drops batch.
        var timedOut = extended.State.Sweep(asks, T0.AddSeconds(33));
        Assert.True(timedOut.Changed);
        Assert.Equal(["b1"], timedOut.DroppedBatchIds);
        var answer = Assert.Single(timedOut.State.Unsent);
        Assert.Equal((AnswerStatus.Unknown, "timeout"), (answer.Status, answer.Error));
    }

    [Fact]
    public void SweepExpiresOldBatchAtMaxAge()
    {
        var asks = new Dictionary<string, IReadOnlyList<Ask>>(StringComparer.Ordinal)
        {
            ["b1"] = [MakeAsk("k0"), MakeAsk("k1")],
        };

        var state = new AsksState()
            .Accept("b1", asks["b1"], new HashSet<string>(StringComparer.Ordinal), T0)
            .State
            .Start(T0, Grace)
            .State;

        var expired = state.Sweep(asks, T0 + AskLimits.MaxAge);

        Assert.True(expired.Changed);
        Assert.Equal(["b1"], expired.DroppedBatchIds);
        Assert.Empty(expired.State.Batches);
        Assert.Equal(2, expired.State.Unsent.Count);
        Assert.All(expired.State.Unsent, a => Assert.Equal((AnswerStatus.Expired, "expired"), (a.Status, a.Error)));
    }
}
