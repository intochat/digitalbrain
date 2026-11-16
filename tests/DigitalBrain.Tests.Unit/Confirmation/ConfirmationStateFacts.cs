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
            var t = state.Request(plan, Requester, "caller-1", ConfirmationPolicy.Always, T0);
            Assert.NotNull(t);
            state = t.Value.Next;
        }

        Assert.Equal(20, Pending(state).Length);

        var plan21 = Plan("cf-21");
        Assert.Throws<InvalidOperationException>(() =>
            state.Request(plan21, Requester, "caller-1", ConfirmationPolicy.Always, T0));
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
        var transition = state.Answer("cf-ans", approve: true, Answerer.Person("user-1"), T1);

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
        var transition = state.Answer("cf-self", approve: true, Answerer.From(appApprover), T1);

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

    [Fact]
    public void RequestPreserves48HourExpiryForRequiresPersonAndCapsAt7Days()
    {
        var state = new ConfirmationState();
        var plan48h = Plan("cf-48h", expiry: TimeSpan.FromHours(48)) with { RequiresPerson = true };

        var t48 = state.Request(plan48h, Requester, "flow-1", ConfirmationPolicy.Always, T0);
        Assert.NotNull(t48);
        var entry48 = Pending(t48.Value.Next)[0];
        Assert.Equal(TimeSpan.FromHours(48), entry48.Plan.Expiry);
        Assert.Equal(T0.AddHours(48), entry48.ExpiresAt);

        var plan14d = Plan("cf-14d", expiry: TimeSpan.FromDays(14)) with { RequiresPerson = true };
        var t14 = t48.Value.Next.Request(plan14d, Requester, "flow-1", ConfirmationPolicy.Always, T0);
        Assert.NotNull(t14);
        var entry14 = Pending(t14.Value.Next).Single(e => e.PlanId == "cf-14d");
        Assert.Equal(TimeSpan.FromDays(7), entry14.Plan.Expiry);
        Assert.Equal(T0.AddDays(7), entry14.ExpiresAt);
    }

    [Fact]
    public void RequestRejectsSameIdWithMismatchedShapeFromSameCaller()
    {
        var state = new ConfirmationState();
        var original = Plan("cf-squat") with
        {
            RequiresPerson = false,
            Kind = "benign",
            Subject = "item-1",
        };
        state = state.Request(original, Requester, "caller-1", ConfirmationPolicy.Always, T0)!.Value.Next;

        // Mismatched RequiresPerson
        Assert.Throws<InvalidOperationException>(() =>
            state.Request(original with { RequiresPerson = true }, Requester, "caller-1", ConfirmationPolicy.Always, T0));

        // Mismatched Kind
        Assert.Throws<InvalidOperationException>(() =>
            state.Request(original with { Kind = "pr-merge" }, Requester, "caller-1", ConfirmationPolicy.Always, T0));

        // Mismatched Subject
        Assert.Throws<InvalidOperationException>(() =>
            state.Request(original with { Subject = "item-2" }, Requester, "caller-1", ConfirmationPolicy.Always, T0));
    }

    [Fact]
    public void BoundNeverEvictsPersonAnsweredEntriesWithUnaskedEntries()
    {
        var state = new ConfirmationState();
        var approver = ConfirmationPrincipal.Person("alice");

        // Record 5 person-answered plans
        for (var i = 0; i < 5; i++)
        {
            var p = Plan($"answered-{i}") with { RequiresPerson = true };
            state = state.Request(p, Requester, "flow", ConfirmationPolicy.Always, T0)!.Value.Next;
            state = state.Answer($"answered-{i}", approve: true, Answerer.From(approver, direct: true), T1)!.Value.Next;
        }

        // Flood with 60 unasked (Skipped = true) entries under Never policy
        for (var i = 0; i < 60; i++)
        {
            var unasked = Plan($"unasked-{i}", destructive: false);
            state = state.Request(unasked, Requester, "app", ConfirmationPolicy.DestructiveOnly, T1)!.Value.Next;
        }

        Assert.Equal(ConfirmationState.MaxEntries, state.Entries.Length);
        for (var i = 0; i < 5; i++)
        {
            var known = state.Known($"answered-{i}");
            Assert.NotNull(known);
            Assert.False(known.Skipped);
            Assert.Equal(ConfirmationOutcome.Approved, known.Outcome);
        }
    }

    [Fact]
    public void RequestCapsPendingPerRaiserAt20AndAllowsDistinctRaisers()
    {
        var state = new ConfirmationState();
        var raiserOne = ConfirmationPrincipal.App("app-1");
        var raiserTwo = ConfirmationPrincipal.App("app-2");

        for (var i = 0; i < ConfirmationState.PendingLimit; i++)
        {
            var transition = state.Request(Plan($"r1-{i}"), raiserOne, "raiser-1", ConfirmationPolicy.Always, T0);
            Assert.NotNull(transition);
            state = transition.Value.Next;
        }

        Assert.Throws<InvalidOperationException>(() =>
            state.Request(Plan("r1-over"), raiserOne, "raiser-1", ConfirmationPolicy.Always, T0));

        var secondRaiser = state.Request(Plan("r2-0"), raiserTwo, "raiser-2", ConfirmationPolicy.Always, T0);
        Assert.NotNull(secondRaiser);
        Assert.Equal(ConfirmationState.PendingLimit + 1, Pending(secondRaiser.Value.Next).Length);
    }

    [Fact]
    public void AnswerWhenRequiresPersonAndNotPersonThrowsWithoutPoisoningPendingPlan()
    {
        var state = new ConfirmationState();
        var plan = Plan("cf-human-only") with { RequiresPerson = true };
        state = state.Request(plan, Requester, "flow-1", ConfirmationPolicy.Always, T0)!.Value.Next;
        var agentApprover = Answerer.Agent("agent-1");
        var relayedPerson = Answerer.RelayedPerson("alice");

        Assert.Throws<UnauthorizedAccessException>(() =>
            state.Answer("cf-human-only", approve: true, agentApprover, T1));
        Assert.Throws<UnauthorizedAccessException>(() =>
            state.Answer("cf-human-only", approve: false, agentApprover, T1));
        Assert.Throws<UnauthorizedAccessException>(() =>
            state.Answer("cf-human-only", approve: true, relayedPerson, T1));

        Assert.Single(Pending(state));
        Assert.Null(state.Known("cf-human-only"));
    }

    [Fact]
    public void AnswerWhenNotPersonDeniesRecordsSelfApprovalNotByPerson()
    {
        var state = new ConfirmationState();
        var plan = Plan("cf-nonperson-deny") with { RequiresPerson = false };
        state = state.Request(plan, Requester, "caller-1", ConfirmationPolicy.Always, T0)!.Value.Next;
        var agentApprover = Answerer.Agent("agent-1");

        var transition = state.Answer("cf-nonperson-deny", approve: false, agentApprover, T1);

        Assert.NotNull(transition);
        Assert.NotNull(transition.Value.Result);
        Assert.Equal(ConfirmationOutcome.Denied, transition.Value.Result.Outcome);
        Assert.Equal(ConfirmationDenyReason.SelfApproval, transition.Value.Result.Reason);
    }

    [Fact]
    public void AnswererOfRejectsForeignBrainAndDerivesDirectPersonAuthority()
    {
        var brain = new BrainId("b1");
        var other = new BrainId("b2");
        var alice = new DigitalBrain.Access.Actor.Person("alice", "acc-1");
        var neuron = new NeuronId(brain, "flow.x", "run-1");

        Assert.Throws<UnauthorizedAccessException>(() =>
            Answerer.Of(new DigitalBrain.Access.Caller(alice, other), brain));
        Assert.True(Answerer.Of(new DigitalBrain.Access.Caller(alice, brain), brain).IsPerson);
        Assert.False(Answerer.Of(new DigitalBrain.Access.Caller(alice, brain, via: neuron), brain).IsPerson);
        Assert.False(Answerer.Of(new DigitalBrain.Access.Caller(alice, brain, conversation: "conv-1"), brain).IsPerson);
        Assert.False(Answerer.Of(new DigitalBrain.Access.Caller(new DigitalBrain.Access.Actor.Agent(alice), brain), brain).IsPerson);
        Assert.False(Answerer.Of(null, brain).IsPerson);
    }
}
