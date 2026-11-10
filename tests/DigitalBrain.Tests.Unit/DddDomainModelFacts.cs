using System.Globalization;
using DigitalBrain.Identity;
using Xunit;

namespace DigitalBrain.Tests.Unit;

public sealed class DddDomainModelFacts
{
    private sealed record CounterState(int Value);
    private sealed record TestSignal(string Name) : Signal;

    [Theory]
    [InlineData("alice", true)]
    [InlineData("@system", true)]
    [InlineData("", false)]
    [InlineData("alice/bob", false)]
    [InlineData("@", false)]
    [InlineData("@sys/tem", false)]
    [InlineData("alice@bob", false)]
    public void BrainId_validates_user_or_system_identifiers(string candidate, bool expected)
    {
        Assert.Equal(expected, BrainId.IsValidUserOrSystem(candidate));
        Assert.Equal(expected, BrainId.TryParseUserOrSystem(candidate, out _));
    }

    [Fact]
    public void ProvisionalGateQueue_tracks_effective_base_and_commits_contiguous_head_runs()
    {
        var now = DateTimeOffset.Parse("2026-11-10T06:00:00Z", CultureInfo.InvariantCulture);
        var queue = new ProvisionalGateQueue<CounterState>();
        var ticket1 = Guid.NewGuid();
        var ticket2 = Guid.NewGuid();
        var initial = new CounterState(10);

        Assert.Equal(initial, queue.EffectiveBase(initial));
        queue.RecordAllowance(new TestSignal("s1"), new CounterState(20), now, ticket1);
        queue.RecordAllowance(new TestSignal("s2"), new CounterState(30), now, ticket2);

        Assert.Equal(new CounterState(30), queue.EffectiveBase(initial));

        // Out-of-order commit of ticket2 stays buffered until ticket1 resolves.
        Assert.True(queue.TryMarkCommitted(ticket2));
        Assert.Null(queue.DequeueCommittedHeadRun());

        // Committing ticket1 flushes both ticket1 and ticket2 in a single batch.
        Assert.True(queue.TryMarkCommitted(ticket1));
        var batch = queue.DequeueCommittedHeadRun();
        Assert.NotNull(batch);
        Assert.Equal(new CounterState(30), batch.Value.Next);
        Assert.Equal(new TestSignal("s2"), batch.Value.Cause);
        Assert.True(queue.WasFlushed(ticket1));
        Assert.True(queue.WasFlushed(ticket2));
    }

    [Fact]
    public void ProvisionalGateQueue_prunes_expired_uncommitted_entries()
    {
        var now = DateTimeOffset.Parse("2026-11-10T06:00:00Z", CultureInfo.InvariantCulture);
        var queue = new ProvisionalGateQueue<CounterState>();
        var expiredTicket = Guid.NewGuid();
        var initial = new CounterState(10);

        queue.RecordAllowance(new TestSignal("s1"), new CounterState(25), now.AddMinutes(-10), expiredTicket);
        Assert.True(queue.PruneExpiredUncommitted(now, TimeSpan.FromMinutes(5)));

        Assert.Equal(initial, queue.EffectiveBase(initial));
        Assert.False(queue.TryMarkCommitted(expiredTicket));
    }

    [Fact]
    public void PoliciesState_evaluates_rules_and_sliding_windows_purely()
    {
        var now = DateTimeOffset.Parse("2026-11-10T12:00:00Z", CultureInfo.InvariantCulture);
        var rule = new PolicyRule(
            "r-1",
            "payments.send",
            PolicyMeasure.Sum,
            new PolicyWindow(PolicyWindowKind.Day, null),
            100m,
            new PolicyCondition("currency", PolicyOp.Equals, "USD"),
            "Allow at most 100 USD of payments.send per day when currency Equals USD",
            now);
        var state = new PoliciesState([rule], [], []);

        var usdFacts = new Dictionary<string, string> { ["currency"] = "USD" };
        var eurFacts = new Dictionary<string, string> { ["currency"] = "EUR" };

        var (afterFirst, firstViolated) = state.TryReserve("res-1", "payments.send", 60m, usdFacts, now);
        Assert.Null(firstViolated);
        Assert.NotNull(afterFirst);

        var (_, overCapViolated) = afterFirst!.TryReserve("res-2", "payments.send", 50m, usdFacts, now.AddHours(1));
        Assert.NotNull(overCapViolated);
        Assert.Equal("r-1", overCapViolated!.Id);

        var (unmatchedNext, unmatchedViolated) = afterFirst.TryReserve("res-3", "payments.send", 50m, eurFacts, now.AddHours(1));
        Assert.Null(unmatchedViolated);
        Assert.Null(unmatchedNext);

        var released = afterFirst.ReleaseReservation("res-1", now.AddHours(1));
        Assert.NotNull(released);

        var (afterRelease, afterReleaseViolated) = released!.TryReserve("res-4", "payments.send", 50m, usdFacts, now.AddHours(1));
        Assert.Null(afterReleaseViolated);
        Assert.NotNull(afterRelease);
    }

    [Fact]
    public void InstallsState_encapsulates_pending_rebind_and_completion_transitions()
    {
        var now = DateTimeOffset.Parse("2026-11-10T12:00:00Z", CultureInfo.InvariantCulture);
        var state = new InstallsState();
        var bindings = new Dictionary<string, string> { ["mail"] = "gmail" };
        var pending = new PendingInstall("plan-1", "digest", bindings, "hash-1", "Needs mail");

        var staged = state.StagePending(pending);
        Assert.Single(staged.Pending);
        Assert.Equal(pending, staged.FindPendingByPlan("plan-1"));

        var needs = new SynapseNeeds("digest", [], [], [], [], false);
        var completed = staged.CompleteInstall(pending, needs, now);
        Assert.Empty(completed.Pending);
        Assert.NotNull(completed.FindRecord("digest"));

        var rebound = completed.Rebind(completed.FindRecord("digest")!, new Dictionary<string, string> { ["mail"] = "outlook" });
        Assert.Equal("outlook", rebound.FindRecord("digest")!.Bindings["mail"]);
    }
}
