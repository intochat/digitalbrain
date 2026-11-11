using DigitalBrain.Confirmation;
using Xunit;

namespace DigitalBrain.Tests.Unit.Confirmation;

public sealed class ConfirmationStateFacts
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T1 = T0.AddMinutes(1);

    private static ConfirmationPlan Plan(string id = "plan-1", bool destructive = true, TimeSpan? expiry = null) =>
        new(id, ConfirmationAction.Delete, destructive, "Title", "Body", "Yes", "No", "Target")
        {
            Expiry = expiry ?? TimeSpan.FromMinutes(2)
        };

    private static ConfirmationRequested[] Pending(ConfirmationState s) =>
        s.Entries.Where(e => e.Result is null).Select(e => e.Request).ToArray();

    private static readonly ConfirmationPrincipal Requester = ConfirmationPrincipal.App("app-1");

    [Fact]
    public void RequestAddsPendingEntryWhenConfirmationRequired()
    {
        var state = new ConfirmationState();
        var plan = Plan("cf-1");

        var transition = state.Request(plan, Requester, "caller-1", ConfirmationPolicy.DestructiveOnly, T0);
        Assert.NotNull(transition);
        Assert.Null(transition.Value.Result);

        var requested = Assert.IsType<ConfirmationRequested>(transition.Value.Signal);
        Assert.Equal("cf-1", requested.PlanId);

        var pending = Pending(transition.Value.Next);
        Assert.Single(pending);
        Assert.Equal("cf-1", pending[0].Plan.Id);
    }

    [Fact]
    public void RequestAutoApprovesWhenPolicyNever()
    {
        var state = new ConfirmationState();
        var plan = Plan("cf-auto");

        var transition = state.Request(plan, Requester, "caller-1", ConfirmationPolicy.Never, T0);
        Assert.NotNull(transition);
        Assert.NotNull(transition.Value.Result);
        Assert.Equal(ConfirmationOutcome.Approved, transition.Value.Result.Outcome);
        Assert.True(transition.Value.Result.Skipped);

        var resolved = Assert.IsType<ConfirmationResolved>(transition.Value.Signal);
        Assert.Equal(ConfirmationOutcome.Approved, resolved.Outcome);
        Assert.True(resolved.Skipped);
        Assert.Empty(Pending(transition.Value.Next));
    }

    [Fact]
    public void RequestEnforces20PendingLimit()
    {
        var state = new ConfirmationState();
        for (var i = 0; i < 20; i++)
        {
            var plan = Plan($"cf-{i}");
            var t = state.Request(plan, Requester, $"caller-{i}", ConfirmationPolicy.Always, T0);
            Assert.NotNull(t);
            state = t.Value.Next;
        }

        Assert.Equal(20, Pending(state).Length);

        var plan21 = Plan("cf-21");
        Assert.Throws<InvalidOperationException>(() =>
            state.Request(plan21, Requester, "caller-21", ConfirmationPolicy.Always, T0));
    }

    [Fact]
    public void RequestEnforces128MaxIdLength()
    {
        var state = new ConfirmationState();
        var longId = new string('x', 129);
        var plan = Plan(longId);

        Assert.Throws<ArgumentException>(() => state.Request(plan, Requester, "caller-1", ConfirmationPolicy.Always, T0));
    }

    [Fact]
    public void RequestCapsExpiryAt5Minutes()
    {
        var state = new ConfirmationState();
        var plan = Plan("cf-long-expiry", expiry: TimeSpan.FromHours(1));

        var transition = state.Request(plan, Requester, "caller-1", ConfirmationPolicy.Always, T0);
        Assert.NotNull(transition);
        var entry = Pending(transition.Value.Next)[0];
        Assert.Equal(T0.AddMinutes(5), entry.ExpiresAt);
    }

    [Fact]
    public void RequestIsIdempotentForSameCaller()
    {
        var state = new ConfirmationState();
        var plan = Plan("cf-idemp");

        var t1 = state.Request(plan, Requester, "caller-same", ConfirmationPolicy.Always, T0)!;
        state = t1.Value.Next;

        var t2 = state.Request(plan, Requester, "caller-same", ConfirmationPolicy.Always, T0);
        Assert.Null(t2);
    }

    [Fact]
    public void RequestRejectsSameIdFromDifferentCaller()
    {
        var state = new ConfirmationState();
        var plan = Plan("cf-diff");

        var t1 = state.Request(plan, Requester, "caller-1", ConfirmationPolicy.Always, T0)!;
        state = t1.Value.Next;

        Assert.Throws<InvalidOperationException>(() =>
            state.Request(plan, Requester, "caller-2", ConfirmationPolicy.Always, T0));
    }

    [Fact]
    public void AnswerResolvesPendingConfirmation()
    {
        var state = new ConfirmationState();
        var plan = Plan("cf-ans");
        state = state.Request(plan, Requester, "caller-1", ConfirmationPolicy.Always, T0)!.Value.Next;

        var approver = ConfirmationPrincipal.Person("user-1");
        var transition = state.Answer("cf-ans", approve: true, person: true, approver, T1);

        Assert.NotNull(transition);
        Assert.NotNull(transition.Value.Result);
        Assert.Equal(ConfirmationOutcome.Approved, transition.Value.Result.Outcome);
        Assert.Empty(Pending(transition.Value.Next));

        var resolved = Assert.IsType<ConfirmationResolved>(transition.Value.Signal);
        Assert.Equal(ConfirmationOutcome.Approved, resolved.Outcome);
        Assert.Equal(approver, resolved.Approver);
    }

    [Fact]
    public void AnswerRejectsSelfApprovalByApp()
    {
        var state = new ConfirmationState();
        var plan = Plan("cf-self");
        state = state.Request(plan, Requester, "caller-1", ConfirmationPolicy.Always, T0)!.Value.Next;

        var appApprover = Requester;
        var transition = state.Answer("cf-self", approve: true, person: false, appApprover, T1);

        Assert.NotNull(transition);
        Assert.NotNull(transition.Value.Result);
        Assert.Equal(ConfirmationOutcome.Denied, transition.Value.Result.Outcome);
        Assert.Equal(ConfirmationDenyReason.SelfApproval, transition.Value.Result.Reason);
    }

    [Fact]
    public void WithdrawCancelsPendingConfirmation()
    {
        var state = new ConfirmationState();
        var plan = Plan("cf-with");
        state = state.Request(plan, Requester, "caller-1", ConfirmationPolicy.Always, T0)!.Value.Next;

        var transition = state.Withdraw("cf-with", T1);
        Assert.NotNull(transition);
        Assert.Equal(ConfirmationOutcome.Denied, transition.Value.Result?.Outcome);
        Assert.Equal(ConfirmationDenyReason.PlanInvalid, transition.Value.Result?.Reason);
        Assert.Empty(Pending(transition.Value.Next));
    }

    [Fact]
    public void ExpireExpiresTimedOutConfirmations()
    {
        var state = new ConfirmationState();
        var plan = Plan("cf-exp", expiry: TimeSpan.FromMinutes(1));
        state = state.Request(plan, Requester, "caller-1", ConfirmationPolicy.Always, T0)!.Value.Next;

        var transition = state.Expire("cf-exp", T0.AddMinutes(2));
        Assert.NotNull(transition);
        Assert.Equal(ConfirmationOutcome.Expired, transition.Value.Result?.Outcome);
        Assert.Equal(ConfirmationDenyReason.Expired, transition.Value.Result?.Reason);
        Assert.Empty(Pending(transition.Value.Next));
    }
}
