using DigitalBrain.Confirmation;
using DigitalBrain.Flows;
using DigitalBrain.Spi;
using DigitalBrain.Tests.Unit.Neurons;

namespace DigitalBrain.Tests.Unit.Flows;

public sealed class Box
{
    public bool AskFirst { get; init; }

    public WaitReason Reason { get; init; } = WaitReason.Approval;

    public TimeSpan? ExpiresIn { get; init; }

    public bool FailB { get; init; }

    public int Runs { get; init; }
}

/// <summary>Two steps; "b" waits when <see cref="Box.AskFirst"/>, fails when <see cref="Box.FailB"/>.</summary>
[Neuron("test.flow")]
public sealed class TwoStepFlow : Flow<Box>
{
    private static readonly Lock Gate = new();

    public static List<string> Calls { get; } = [];

    /// <summary>The number of bodies still to hang until their token is cancelled.</summary>
    public static int HangsLeft { get; set; }

    public static bool SawCancellation { get; set; }

    /// <summary>When set, bodies wait for it before they run.</summary>
    public static Task? Hold { get; set; }

    /// <summary>The number of bodies still to throw an <see cref="OperationCanceledException"/> of a token not the flow's.</summary>
    public static int ForeignCancelsLeft { get; set; }

    public FlowState<Box> Current => State;

    protected override IReadOnlyList<string> Steps => ["a", "b"];

    protected override async Task<StepResult<Box>> Run(string step, Box inner, CancellationToken ct)
    {
        lock (Gate) { Calls.Add(step); }
        if (Hold is { } gate) { await gate; }
        if (ForeignCancelsLeft > 0)
        {
            ForeignCancelsLeft--;
            using var foreign = new CancellationTokenSource();
            await foreign.CancelAsync();
            foreign.Token.ThrowIfCancellationRequested();
        }
        if (HangsLeft > 0)
        {
            HangsLeft--;
            try { await Task.Delay(Timeout.Infinite, ct); }
            catch (OperationCanceledException) { SawCancellation = true; throw; }
        }
        var next = new Box { AskFirst = inner.AskFirst, Reason = inner.Reason, ExpiresIn = inner.ExpiresIn, FailB = inner.FailB, Runs = inner.Runs + 1 };
        if (step == "b" && inner.FailB) { throw new FlowFailed("b", "x"); }
        if (step == "b" && inner.AskFirst)
        {
            var expires = inner.ExpiresIn is { } span ? Context.Time.GetUtcNow() + span : (DateTimeOffset?)null;
            return new StepResult<Box>(next, new Waiting("b", inner.Reason, expires, null));
        }
        return new StepResult<Box>(next);
    }

    public Task Deliver(ConfirmationResolved resolved) => OnConfirmation(resolved);

    public Task Tick(string name) => NeuronAttachment.Remind(this, name, CancellationToken.None);
}

internal sealed class RecordingConfirmations : IConfirmations
{
    public List<ConfirmationPlan> Asked { get; } = [];

    public List<(string PlanId, bool Approve)> Resolved { get; } = [];

    public ConfirmationPolicy Policy => ConfirmationPolicy.DestructiveOnly;

    // Never answers: the flow must not await it inside the turn.
    public Task<ConfirmationResult> Confirm(ConfirmationPlan plan, CancellationToken ct = default)
    {
        Asked.Add(plan);
        return new TaskCompletionSource<ConfirmationResult>().Task;
    }

    public Task Resolve(string planId, bool approve, CancellationToken ct = default)
    {
        Resolved.Add((planId, approve));
        return Task.CompletedTask;
    }
}

internal sealed class ThrowingConfirmations : IConfirmations
{
    public ConfirmationPolicy Policy => ConfirmationPolicy.DestructiveOnly;

    public Task<ConfirmationResult> Confirm(ConfirmationPlan plan, CancellationToken ct = default) => throw new InvalidOperationException("inbox down");

    public Task Resolve(string planId, bool approve, CancellationToken ct = default) => Task.CompletedTask;
}

// Answers at once, as a policy that skips the question does, and never publishes ConfirmationResolved.
internal sealed class SkippingConfirmations(ConfirmationOutcome outcome) : IConfirmations
{
    public ConfirmationPolicy Policy => ConfirmationPolicy.Never;

    public Task<ConfirmationResult> Confirm(ConfirmationPlan plan, CancellationToken ct = default)
        => Task.FromResult(new ConfirmationResult(plan.Id, outcome, outcome == ConfirmationOutcome.Approved ? ConfirmationDenyReason.None : ConfirmationDenyReason.PlanInvalid, Skipped: true));

    public Task Resolve(string planId, bool approve, CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class RecordingReminders : IReminders
{
    public Dictionary<string, TimeSpan> Started { get; } = [];

    public List<string> Stopped { get; } = [];

    public Task Start(string name, TimeSpan due, TimeSpan period)
    {
        Started[name] = due;
        return Task.CompletedTask;
    }

    public Task Stop(string name)
    {
        Stopped.Add(name);
        _ = Started.Remove(name);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> Names() => Task.FromResult<IReadOnlyList<string>>([.. Started.Keys]);
}

internal sealed class ManualClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;
}

public sealed class FlowFacts
{
    private static readonly NeuronId FlowId = new(new BrainId("alice"), "test.flow", "f1");

    private readonly RecordingConfirmations _confirmations = new();
    private readonly RecordingReminders _reminders = new();
    private readonly ManualClock _clock = new();

    public FlowFacts()
    {
        TwoStepFlow.Calls.Clear();
        TwoStepFlow.HangsLeft = 0;
        TwoStepFlow.SawCancellation = false;
        TwoStepFlow.Hold = null;
        TwoStepFlow.ForeignCancelsLeft = 0;
    }

    private async Task<(TwoStepFlow Flow, RecordingContext Context)> Activated(object? stored = null, IConfirmations? confirmations = null)
    {
        var context = new RecordingContext(FlowId) { RecordedConfirmations = confirmations ?? _confirmations, RecordedReminders = _reminders, Time = _clock };
        if (stored is not null) { context.Store(stored); }
        var flow = new TwoStepFlow();
        NeuronAttachment.Attach(flow, context);
        context.Neurons[FlowId] = flow;
        await NeuronAttachment.Activate(flow, CancellationToken.None);
        return (flow, context);
    }

    // Runs the turns Start deferred (the self-call that advances the flow), as the runtime does after the turn ends.
    private static async Task Drain(RecordingContext context)
    {
        while (context.Deferred.Count > 0)
        {
            var work = context.Deferred[0];
            context.Deferred.RemoveAt(0);
            await work();
        }
    }

    private static async Task Started(TwoStepFlow flow, RecordingContext context, string title, string owner, string? parent, Box inner)
    {
        await flow.Start(title, owner, parent, inner);
        await Drain(context);
    }

    private static List<Signal> Signals(RecordingContext context) => [.. context.Writes.SelectMany(write => write.Signals)];

    private static ConfirmationResolved Resolution(string planId, ConfirmationOutcome outcome) => new(planId, ConfirmationAction.Other, "f", ConfirmationPrincipal.App("test.flow"),
        outcome == ConfirmationOutcome.Approved ? ConfirmationPrincipal.Person("ann") : null, ConfirmationPolicy.DestructiveOnly, outcome,
        outcome == ConfirmationOutcome.Approved ? ConfirmationDenyReason.None : ConfirmationDenyReason.ByPerson, false, DateTimeOffset.UnixEpoch);

    [Fact]
    public async Task StartRunsStepsInOrderAndJournalsFlowStep()
    {
        var (flow, context) = await Activated();

        await Started(flow, context, "Build", "ann", null, new Box());

        var id = FlowId.ToString();
        Assert.Equal(
            [
                new FlowStarted(id, "test.flow", "Build"),
                new FlowStep(id, "a", StepOutcome.Started, null), new FlowStep(id, "a", StepOutcome.Completed, null),
                new FlowStep(id, "b", StepOutcome.Started, null), new FlowStep(id, "b", StepOutcome.Completed, null),
                new FlowDone(id, FlowOutcome.Completed, null),
            ],
            Signals(context));
        Assert.Equal(["a", "b"], TwoStepFlow.Calls);
        Assert.Equal(FlowStatus.Completed, flow.Current.Status);
        Assert.Equal(["a", "b"], flow.Current.Done);
        Assert.Equal(2, flow.Current.Inner.Runs);
        Assert.False(string.IsNullOrEmpty(flow.Current.TraceId));
        Assert.Equal(_clock.Now, flow.Current.Ended);
    }

    [Fact]
    public async Task ThrowingBodyFailsFlow()
    {
        var (flow, context) = await Activated();

        await Started(flow, context, "Build", "ann", null, new Box { FailB = true });

        var id = FlowId.ToString();
        var signals = Signals(context);
        Assert.Contains(new FlowStep(id, "b", StepOutcome.Failed, "x"), signals);
        Assert.Equal(new FlowDone(id, FlowOutcome.Failed, "x"), signals[^1]);
        Assert.Equal(FlowStatus.Failed, flow.Current.Status);
        Assert.Equal(["a"], flow.Current.Done);
    }

    [Fact]
    public async Task WaitRaisesConfirmationAndResumesOnApprove()
    {
        var (flow, context) = await Activated();

        await Started(flow, context, "Build", "ann", null, new Box { AskFirst = true });

        var waiting = Assert.IsType<Waiting>(flow.Current.Waiting);
        var plan = Assert.Single(_confirmations.Asked);
        Assert.Equal(plan.Id, waiting.ConfirmationPlanId);
        Assert.Equal(ConfirmationAction.Other, plan.Action);
        Assert.Equal("Build", plan.Title);
        Assert.Equal("b", plan.Body);
        Assert.True(plan.RequiresPerson);
        Assert.Equal(FlowStatus.Waiting, flow.Current.Status);
        var id = FlowId.ToString();
        Assert.Contains(new FlowWaiting(id, "b", WaitReason.Approval, plan.Id), Signals(context));

        await flow.Deliver(Resolution(plan.Id, ConfirmationOutcome.Approved));

        var signals = Signals(context);
        Assert.Contains(new FlowResolve(id, "b", WaitOutcome.Approved, "ann"), signals);
        Assert.Contains(new FlowStep(id, "b", StepOutcome.Completed, null), signals);
        Assert.Equal(new FlowDone(id, FlowOutcome.Completed, null), signals[^1]);
        Assert.Equal(["a", "b"], flow.Current.Done);
        Assert.Null(flow.Current.Waiting);
        Assert.Single(_confirmations.Asked);
    }

    [Fact]
    public async Task DenyCancels()
    {
        var (flow, context) = await Activated();
        await Started(flow, context, "Build", "ann", null, new Box { AskFirst = true });
        var plan = Assert.Single(_confirmations.Asked);

        await flow.Deliver(Resolution(plan.Id, ConfirmationOutcome.Denied));

        var id = FlowId.ToString();
        var signals = Signals(context);
        Assert.Contains(new FlowResolve(id, "b", WaitOutcome.Denied, null), signals);
        Assert.Equal(FlowOutcome.Cancelled, Assert.IsType<FlowDone>(signals[^1]).Outcome);
        Assert.Equal(FlowStatus.Cancelled, flow.Current.Status);
        Assert.Equal(["a"], flow.Current.Done);
    }

    [Fact]
    public async Task ReactivationRerunsCurrentStepOnce()
    {
        var (first, context) = await Activated();
        TwoStepFlow.HangsLeft = 1;
        await first.Start("Build", "ann", null, new Box());
        var started = Drain(context);
        Assert.Contains(new FlowStep(FlowId.ToString(), "a", StepOutcome.Started, null), Signals(context));

        await NeuronAttachment.Deactivate(first, DeactivationCause.Idle, CancellationToken.None);
        await started;
        Assert.Equal("a", first.Current.Current);
        Assert.Empty(first.Current.Done);

        var (second, _) = await Activated(context.Writes[^1].State);

        Assert.Equal(["a", "a", "b"], TwoStepFlow.Calls);
        Assert.Equal(["a", "b"], second.Current.Done);
        Assert.Equal(FlowStatus.Completed, second.Current.Status);
    }

    [Fact]
    public async Task UnknownOrRepeatedResolutionIsIgnored()
    {
        var (flow, context) = await Activated();
        await Started(flow, context, "Build", "ann", null, new Box { AskFirst = true });
        var plan = Assert.Single(_confirmations.Asked);
        var before = context.Writes.Count;

        await flow.Deliver(Resolution("other", ConfirmationOutcome.Approved));
        Assert.Equal(before, context.Writes.Count);
        Assert.Equal(FlowStatus.Waiting, flow.Current.Status);

        await flow.Deliver(Resolution(plan.Id, ConfirmationOutcome.Approved));
        var after = context.Writes.Count;
        await flow.Deliver(Resolution(plan.Id, ConfirmationOutcome.Denied));

        Assert.Equal(after, context.Writes.Count);
        Assert.Equal(FlowStatus.Completed, flow.Current.Status);
    }

    [Fact]
    public async Task ExpiryReminderExpiresWait()
    {
        var (flow, context) = await Activated();
        await Started(flow, context, "Build", "ann", null, new Box { AskFirst = true, Reason = WaitReason.External, ExpiresIn = TimeSpan.FromMinutes(10) });
        Assert.Empty(_confirmations.Asked);
        Assert.Equal(TimeSpan.FromMinutes(10), _reminders.Started["flow-expiry"]);

        _clock.Now += TimeSpan.FromMinutes(5);
        await flow.Tick("flow-expiry");
        Assert.Equal(FlowStatus.Waiting, flow.Current.Status);

        _clock.Now += TimeSpan.FromMinutes(6);
        await flow.Tick("flow-expiry");

        var id = FlowId.ToString();
        var signals = Signals(context);
        Assert.Contains(new FlowResolve(id, "b", WaitOutcome.Expired, null), signals);
        Assert.Equal(FlowOutcome.Cancelled, Assert.IsType<FlowDone>(signals[^1]).Outcome);
        Assert.Equal(FlowStatus.Cancelled, flow.Current.Status);
        Assert.Contains("flow-expiry", _reminders.Stopped);
    }

    [Fact]
    public async Task ApprovalWaitUsesPlanExpiry()
    {
        var (flow, context) = await Activated();

        await Started(flow, context, "Build", "ann", null, new Box { AskFirst = true, ExpiresIn = TimeSpan.FromMinutes(30) });

        Assert.Equal(TimeSpan.FromMinutes(30), Assert.Single(_confirmations.Asked).Expiry);
        Assert.Empty(_reminders.Started);
    }

    [Fact]
    public async Task CancelCancelsRunningBodyToken()
    {
        var (flow, context) = await Activated();
        TwoStepFlow.HangsLeft = 1;
        await flow.Start("Build", "ann", null, new Box());
        var started = Drain(context);

        await flow.Cancel("stop");
        await started;

        Assert.True(TwoStepFlow.SawCancellation);
        Assert.Equal(FlowStatus.Cancelled, flow.Current.Status);
        Assert.Equal(new FlowDone(FlowId.ToString(), FlowOutcome.Cancelled, "stop"), Signals(context)[^1]);
        Assert.Equal(["a"], TwoStepFlow.Calls);
    }

    [Fact]
    public async Task CancelResolvesPendingApprovalPlan()
    {
        var (flow, context) = await Activated();
        await Started(flow, context, "Build", "ann", null, new Box { AskFirst = true });
        var plan = Assert.Single(_confirmations.Asked);

        await flow.Cancel("stop");

        Assert.Equal([(plan.Id, false)], _confirmations.Resolved);
        Assert.Equal(FlowStatus.Cancelled, flow.Current.Status);
    }

    [Fact]
    public async Task RelateAppendsRelation()
    {
        var (flow, context) = await Activated();
        await Started(flow, context, "Build", "ann", "parent", new Box { AskFirst = true });
        var relation = new FlowRelation("other", RelationKind.BlockedBy, null);

        await flow.Relate(relation);
        await flow.Relate(relation);

        Assert.Equal([relation], flow.Current.Relations);
        Assert.Single(Signals(context), signal => signal == new FlowRelated(FlowId.ToString(), relation));
        Assert.Equal("parent", flow.Current.ParentFlowId);
    }

    [Fact]
    public async Task ScheduleWaitResumesOnExpiry()
    {
        var (flow, context) = await Activated();
        await Started(flow, context, "Build", "ann", null, new Box { AskFirst = true, Reason = WaitReason.Schedule, ExpiresIn = TimeSpan.FromMinutes(10) });

        _clock.Now += TimeSpan.FromMinutes(11);
        await flow.Tick("flow-expiry");

        var id = FlowId.ToString();
        var signals = Signals(context);
        Assert.Contains(new FlowResolve(id, "b", WaitOutcome.Arrived, null), signals);
        Assert.Contains(new FlowStep(id, "b", StepOutcome.Completed, null), signals);
        Assert.Equal(new FlowDone(id, FlowOutcome.Completed, null), signals[^1]);
        Assert.Equal(["a", "b"], flow.Current.Done);
    }

    [Fact]
    public async Task FailedConfirmationFailsTheFlow()
    {
        var (flow, context) = await Activated(confirmations: new ThrowingConfirmations());

        await Started(flow, context, "Build", "ann", null, new Box { AskFirst = true });

        var id = FlowId.ToString();
        var signals = Signals(context);
        Assert.Contains(new FlowStep(id, "b", StepOutcome.Failed, "confirmation: inbox down"), signals);
        Assert.Equal(new FlowDone(id, FlowOutcome.Failed, "confirmation"), signals[^1]);
        Assert.Equal(FlowStatus.Failed, flow.Current.Status);
        Assert.Null(flow.Current.Waiting);
    }

    [Fact]
    public async Task ConfirmResultWithoutResolutionResumesTheFlow()
    {
        var (flow, context) = await Activated(confirmations: new SkippingConfirmations(ConfirmationOutcome.Approved));

        await Started(flow, context, "Build", "ann", null, new Box { AskFirst = true });

        var id = FlowId.ToString();
        Assert.Contains(new FlowResolve(id, "b", WaitOutcome.Approved, null), Signals(context));
        Assert.Equal(FlowStatus.Completed, flow.Current.Status);
    }

    [Fact]
    public async Task ConfirmResultRefusedUpFrontCancelsTheFlow()
    {
        var (flow, context) = await Activated(confirmations: new SkippingConfirmations(ConfirmationOutcome.Denied));

        await Started(flow, context, "Build", "ann", null, new Box { AskFirst = true });

        Assert.Contains(new FlowResolve(FlowId.ToString(), "b", WaitOutcome.Denied, null), Signals(context));
        Assert.Equal(FlowStatus.Cancelled, flow.Current.Status);
    }

    [Fact]
    public async Task StartReturnsBeforeTheFirstStepRuns()
    {
        var (flow, context) = await Activated();
        var gate = new TaskCompletionSource();
        TwoStepFlow.Hold = gate.Task;

        await flow.Start("Build", "ann", null, new Box());

        Assert.Equal(FlowStatus.Running, flow.Current.Status);
        Assert.Null(flow.Current.Current);
        Assert.Empty(TwoStepFlow.Calls);
        Assert.Equal([new FlowStarted(FlowId.ToString(), "test.flow", "Build")], Signals(context));
        var advancing = Drain(context);
        Assert.Equal("a", flow.Current.Current);
        Assert.False(advancing.IsCompleted);

        gate.SetResult();
        await advancing;
        Assert.Equal(FlowStatus.Completed, flow.Current.Status);
    }

    [Fact]
    public async Task ActivationAdvancesAStartedFlowThatNeverRan()
    {
        var (first, context) = await Activated();
        await first.Start("Build", "ann", null, new Box());
        context.Deferred.Clear(); // the activation died before the deferred advance ran
        Assert.Equal(FlowStatus.Running, first.Current.Status);
        Assert.Empty(first.Current.Done);
        Assert.Null(first.Current.Current);

        var (second, _) = await Activated(context.Writes[^1].State);

        Assert.Equal(["a", "b"], TwoStepFlow.Calls);
        Assert.Equal(FlowStatus.Completed, second.Current.Status);
    }

    [Fact]
    public async Task CancelledBodyIsNotFailed()
    {
        var (first, context) = await Activated();
        TwoStepFlow.ForeignCancelsLeft = 1;

        await Started(first, context, "Build", "ann", null, new Box());

        Assert.Equal(FlowStatus.Running, first.Current.Status);
        Assert.Equal("a", first.Current.Current);
        Assert.DoesNotContain(Signals(context), signal => signal is FlowStep { Outcome: StepOutcome.Failed } or FlowDone);

        var (second, _) = await Activated(context.Writes[^1].State);

        Assert.Equal(["a", "a", "b"], TwoStepFlow.Calls);
        Assert.Equal(FlowStatus.Completed, second.Current.Status);
    }

    [Fact]
    public async Task NudgeReminderRerunsAnInterruptedStep()
    {
        var (flow, context) = await Activated();
        TwoStepFlow.ForeignCancelsLeft = 1;
        await Started(flow, context, "Build", "ann", null, new Box());
        Assert.Equal(FlowStatus.Running, flow.Current.Status);
        Assert.Equal("a", flow.Current.Current);
        Assert.Equal(TimeSpan.FromMinutes(1), _reminders.Started["flow-nudge"]);

        await flow.Tick("flow-nudge");

        Assert.Equal(["a", "a", "b"], TwoStepFlow.Calls);
        Assert.Equal(FlowStatus.Completed, flow.Current.Status);
        Assert.DoesNotContain("flow-nudge", _reminders.Started.Keys);

        await flow.Tick("flow-nudge");
        Assert.Equal(["a", "a", "b"], TwoStepFlow.Calls);
    }

    [Fact]
    public async Task NudgeStopsWhileWaitingAndRestartsOnResume()
    {
        var (flow, context) = await Activated();
        await Started(flow, context, "Build", "ann", null, new Box { AskFirst = true, Reason = WaitReason.External });
        Assert.DoesNotContain("flow-nudge", _reminders.Started.Keys);

        await flow.Resume("b", WaitOutcome.Arrived, "ann");

        Assert.Equal(FlowStatus.Completed, flow.Current.Status);
        Assert.Equal(2, _reminders.Stopped.Count(name => name == "flow-nudge"));
    }

    [Fact]
    public async Task FailedAdvancePostDoesNotFailStart()
    {
        var (flow, context) = await Activated();
        context.Neurons.Remove(FlowId); // the self-call cannot be posted

        await Started(flow, context, "Build", "ann", null, new Box());

        Assert.Equal(FlowStatus.Running, flow.Current.Status);
        Assert.Empty(TwoStepFlow.Calls);
        Assert.Contains("flow-nudge", _reminders.Started.Keys);
    }
}
