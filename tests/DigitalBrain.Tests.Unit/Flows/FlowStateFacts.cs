using DigitalBrain.Flows;
using Xunit;

namespace DigitalBrain.Tests.Unit.Flows;

public sealed class FlowStateFacts
{
    private sealed record TestInner;

    [Fact]
    public void Start_WhenPending_SetsRunningAndEmitsStarted()
    {
        var state = new FlowState<TestInner>();
        var now = DateTimeOffset.UtcNow;
        var transition = state.Start("Sample Flow", "user1", null, new TestInner(), now, "trace1", "test.flow");

        Assert.NotNull(transition);
        Assert.Equal(FlowStatus.Running, transition.Value.Next.Status);
        Assert.Equal("Sample Flow", transition.Value.Next.Title);
        Assert.Equal("user1", transition.Value.Next.Owner);
        Assert.Equal(now, transition.Value.Next.Started);
        var signal = Assert.IsType<FlowStarted>(Assert.Single(transition.Value.Signals));
        Assert.Equal("Sample Flow", signal.Title);
    }

    [Fact]
    public void Start_WhenNotPending_ReturnsNull()
    {
        var now = DateTimeOffset.UtcNow;
        var running = new FlowState<TestInner>().Start("T", "O", null, new TestInner(), now, null, "test")!.Value.Next;
        Assert.Null(running.Start("T2", "O2", null, new TestInner(), now, null, "test"));
    }

    [Fact]
    public void RequestCancel_WhenActive_SetsCancelledAndEmitsDone()
    {
        var now = DateTimeOffset.UtcNow;
        var running = new FlowState<TestInner>().Start("T", "O", null, new TestInner(), now, null, "test")!.Value.Next;
        var cancel = running.RequestCancel("user requested", now);

        Assert.NotNull(cancel);
        Assert.Equal(FlowStatus.Cancelled, cancel.Value.Next.Status);
        Assert.Equal(now, cancel.Value.Next.Ended);
        Assert.True(cancel.Value.Next.IsOver);
        var signal = Assert.IsType<FlowDone>(Assert.Single(cancel.Value.Signals));
        Assert.Equal(FlowOutcome.Cancelled, signal.Outcome);
        Assert.Equal("user requested", signal.Detail);
    }

    [Fact]
    public void RequestCancel_WhenOver_ReturnsNull()
    {
        var now = DateTimeOffset.UtcNow;
        var cancelled = new FlowState<TestInner>()
            .Start("T", "O", null, new TestInner(), now, null, "test")!.Value.Next
            .RequestCancel("r", now)!.Value.Next;

        Assert.Null(cancelled.RequestCancel("again", now));
    }

    [Fact]
    public void StartStep_WhenStepsRemaining_StartsNextStep()
    {
        var now = DateTimeOffset.UtcNow;
        var running = new FlowState<TestInner>().Start("T", "O", null, new TestInner(), now, null, "test")!.Value.Next;
        var step1 = running.StartStep(["stepA", "stepB"], now);

        Assert.NotNull(step1);
        Assert.Equal("stepA", step1.Value.Next.Current);
        var signal = Assert.IsType<FlowStep>(Assert.Single(step1.Value.Signals));
        Assert.Equal("stepA", signal.Step);
        Assert.Equal(StepOutcome.Started, signal.Outcome);
    }

    [Fact]
    public void StartStep_WhenNoStepsRemaining_CompletesFlow()
    {
        var now = DateTimeOffset.UtcNow;
        var running = new FlowState<TestInner>().Start("T", "O", null, new TestInner(), now, null, "test")!.Value.Next;
        var completed = running.StartStep([], now);

        Assert.NotNull(completed);
        Assert.Equal(FlowStatus.Completed, completed.Value.Next.Status);
        Assert.Null(completed.Value.Next.Current);
        Assert.Equal(now, completed.Value.Next.Ended);
        var signal = Assert.IsType<FlowDone>(Assert.Single(completed.Value.Signals));
        Assert.Equal(FlowOutcome.Completed, signal.Outcome);
    }

    [Fact]
    public void Complete_WhenStepMatches_AdvancesStepAndEmits()
    {
        var now = DateTimeOffset.UtcNow;
        var running = new FlowState<TestInner>().Start("T", "O", null, new TestInner(), now, null, "test")!.Value.Next;
        var withStep = running.StartStep(["stepA"], now)!.Value.Next;

        var result = new StepResult<TestInner>(new TestInner());
        var completed = withStep.Complete("stepA", result);

        Assert.NotNull(completed);
        Assert.Null(completed.Value.Next.Current);
        Assert.Contains("stepA", completed.Value.Next.Done);
        var signal = Assert.IsType<FlowStep>(Assert.Single(completed.Value.Signals));
        Assert.Equal("stepA", signal.Step);
        Assert.Equal(StepOutcome.Completed, signal.Outcome);
    }

    [Fact]
    public void Complete_WhenStepDoesNotMatch_ReturnsNull()
    {
        var now = DateTimeOffset.UtcNow;
        var running = new FlowState<TestInner>().Start("T", "O", null, new TestInner(), now, null, "test")!.Value.Next;
        var withStep = running.StartStep(["stepA"], now)!.Value.Next;

        Assert.Null(withStep.Complete("differentStep", new StepResult<TestInner>(new TestInner())));
    }

    [Fact]
    public void Wait_WhenStepMatches_EntersWaitingAndEmits()
    {
        var now = DateTimeOffset.UtcNow;
        var running = new FlowState<TestInner>().Start("T", "O", null, new TestInner(), now, null, "test")!.Value.Next;
        var withStep = running.StartStep(["stepA"], now)!.Value.Next;

        var waiting = new Waiting("stepA", WaitReason.Approval, now.AddMinutes(5), "plan123");
        var wait = withStep.Wait("stepA", new StepResult<TestInner>(new TestInner(), waiting), waiting, "plan123");

        Assert.NotNull(wait);
        Assert.Equal(FlowStatus.Waiting, wait.Value.Next.Status);
        Assert.Equal("plan123", wait.Value.Next.Waiting?.ConfirmationPlanId);
        var signal = Assert.IsType<FlowWaiting>(Assert.Single(wait.Value.Signals));
        Assert.Equal("plan123", signal.ConfirmationPlanId);
    }

    [Fact]
    public void Fail_WhenActive_SetsFailedAndEmits()
    {
        var now = DateTimeOffset.UtcNow;
        var running = new FlowState<TestInner>().Start("T", "O", null, new TestInner(), now, null, "test")!.Value.Next;
        var withStep = running.StartStep(["stepA"], now)!.Value.Next;

        var fail = withStep.Fail("stepA", "something broke", now);
        Assert.NotNull(fail);
        Assert.Equal(FlowStatus.Failed, fail.Value.Next.Status);
        Assert.Equal(now, fail.Value.Next.Ended);
        Assert.Equal(2, fail.Value.Signals.Count);
        Assert.IsType<FlowStep>(fail.Value.Signals[0]);
        Assert.IsType<FlowDone>(fail.Value.Signals[1]);
    }

    [Fact]
    public void Interrupted_IncrementsCountAndEmitsInterrupted()
    {
        var now = DateTimeOffset.UtcNow;
        var running = new FlowState<TestInner>().Start("T", "O", null, new TestInner(), now, null, "test")!.Value.Next;
        var withStep = running.StartStep(["stepA"], now)!.Value.Next;

        var interrupted = withStep.Interrupted("stepA", "reboot", now, maxInterruptions: 5);
        Assert.NotNull(interrupted);
        Assert.Equal(1, interrupted.Value.Next.Interruptions);
        Assert.Equal(FlowStatus.Running, interrupted.Value.Next.Status);
        var signal = Assert.IsType<FlowStep>(Assert.Single(interrupted.Value.Signals));
        Assert.Equal(StepOutcome.Interrupted, signal.Outcome);
    }

    [Fact]
    public void Interrupted_WhenMaxExceeded_FailsFlow()
    {
        var now = DateTimeOffset.UtcNow;
        var running = new FlowState<TestInner>().Start("T", "O", null, new TestInner(), now, null, "test")!.Value.Next;
        var withStep = (running.StartStep(["stepA"], now)!.Value.Next) with { Interruptions = 4 };

        var interrupted = withStep.Interrupted("stepA", "reboot again", now, maxInterruptions: 5);
        Assert.NotNull(interrupted);
        Assert.Equal(5, interrupted.Value.Next.Interruptions);
        Assert.Equal(FlowStatus.Failed, interrupted.Value.Next.Status);
        Assert.Equal(now, interrupted.Value.Next.Ended);
        Assert.Equal(2, interrupted.Value.Signals.Count);
    }

    [Fact]
    public void Resolve_ApprovedOrArrived_ResumesRunningAndCompletesStep()
    {
        var now = DateTimeOffset.UtcNow;
        var running = new FlowState<TestInner>().Start("T", "O", null, new TestInner(), now, null, "test")!.Value.Next;
        var withStep = running.StartStep(["stepA"], now)!.Value.Next;
        var waiting = new Waiting("stepA", WaitReason.Approval, now.AddMinutes(5), "plan1");
        var inWait = withStep.Wait("stepA", new StepResult<TestInner>(new TestInner(), waiting), waiting, "plan1")!.Value.Next;

        var resolve = inWait.Resolve("stepA", WaitOutcome.Approved, "person1", now);
        Assert.NotNull(resolve);
        Assert.Equal(FlowStatus.Running, resolve.Value.Next.Status);
        Assert.Null(resolve.Value.Next.Waiting);
        Assert.Contains("stepA", resolve.Value.Next.Done);
        Assert.Equal(2, resolve.Value.Signals.Count);
        var res = Assert.IsType<FlowResolve>(resolve.Value.Signals[0]);
        Assert.Equal(WaitOutcome.Approved, res.Outcome);
        Assert.Equal("person1", res.By);
    }

    [Fact]
    public void Resolve_DeniedOrCancelled_CancelsFlow()
    {
        var now = DateTimeOffset.UtcNow;
        var running = new FlowState<TestInner>().Start("T", "O", null, new TestInner(), now, null, "test")!.Value.Next;
        var withStep = running.StartStep(["stepA"], now)!.Value.Next;
        var waiting = new Waiting("stepA", WaitReason.Approval, now.AddMinutes(5), "plan1");
        var inWait = withStep.Wait("stepA", new StepResult<TestInner>(new TestInner(), waiting), waiting, "plan1")!.Value.Next;

        var resolve = inWait.Resolve("stepA", WaitOutcome.Denied, "person1", now);
        Assert.NotNull(resolve);
        Assert.Equal(FlowStatus.Cancelled, resolve.Value.Next.Status);
        Assert.Null(resolve.Value.Next.Waiting);
        Assert.Equal(now, resolve.Value.Next.Ended);
    }

    [Fact]
    public void Relate_AddsRelationAndEmits()
    {
        var state = new FlowState<TestInner>();
        var rel = new FlowRelation("flow2", RelationKind.Blocks, null);
        var transition = state.Relate(rel);

        Assert.NotNull(transition);
        Assert.Contains(rel, transition.Value.Next.Relations);
        var signal = Assert.IsType<FlowRelated>(Assert.Single(transition.Value.Signals));
        Assert.Equal(rel, signal.Relation);
    }

    [Fact]
    public void Relate_WhenAlreadyPresent_ReturnsNull()
    {
        var rel = new FlowRelation("flow2", RelationKind.Blocks, null);
        var state = new FlowState<TestInner>().Relate(rel)!.Value.Next;
        Assert.Null(state.Relate(rel));
    }

    [Fact]
    public void Skip_MarksStepDoneAndEmitsSkipped()
    {
        var now = DateTimeOffset.UtcNow;
        var running = new FlowState<TestInner>().Start("T", "O", null, new TestInner(), now, null, "test")!.Value.Next;
        var withStep = running.StartStep(["stepA", "stepB"], now)!.Value.Next;

        var result = StepResult<TestInner>.Skip(new TestInner(), "condition not met");
        var transition = withStep.Skip("stepA", result, result.Reason);

        Assert.NotNull(transition);
        Assert.Contains("stepA", transition.Value.Next.Done);
        Assert.Null(transition.Value.Next.Current);
        var signal = Assert.IsType<FlowStep>(Assert.Single(transition.Value.Signals));
        Assert.Equal(StepOutcome.Skipped, signal.Outcome);
        Assert.Equal("condition not met", signal.Detail);

        var nextStep = transition.Value.Next.StartStep(["stepA", "stepB"], now);
        Assert.NotNull(nextStep);
        Assert.Equal("stepB", nextStep.Value.Next.Current);
    }

    [Fact]
    public void Repeat_KeepsStepNotDoneAndEmitsRepeated()
    {
        var now = DateTimeOffset.UtcNow;
        var running = new FlowState<TestInner>().Start("T", "O", null, new TestInner(), now, null, "test")!.Value.Next;
        var withStep = running.StartStep(["stepA"], now)!.Value.Next;

        var result = StepResult<TestInner>.Repeat(new TestInner());
        var transition = withStep.Repeat("stepA", result);

        Assert.NotNull(transition);
        Assert.DoesNotContain("stepA", transition.Value.Next.Done);
        Assert.Null(transition.Value.Next.Current);
        var signal = Assert.IsType<FlowStep>(Assert.Single(transition.Value.Signals));
        Assert.Equal(StepOutcome.Repeated, signal.Outcome);

        var nextStep = transition.Value.Next.StartStep(["stepA"], now);
        Assert.NotNull(nextStep);
        Assert.Equal("stepA", nextStep.Value.Next.Current);
    }

    [Fact]
    public void WaitForChildren_TransitionsToWaitingWithReasonChildren()
    {
        var now = DateTimeOffset.UtcNow;
        var running = new FlowState<TestInner>().Start("T", "O", null, new TestInner(), now, null, "test")!.Value.Next;
        var withStep = running.StartStep(["stepA"], now)!.Value.Next;

        var result = StepResult<TestInner>.WaitForChildren(new TestInner(), ["child-run-1", "child-run-2"]);
        var transition = withStep.Wait("stepA", result, result.Wait!, null);

        Assert.NotNull(transition);
        Assert.Equal(FlowStatus.Waiting, transition.Value.Next.Status);
        Assert.NotNull(transition.Value.Next.Waiting);
        Assert.Equal(WaitReason.Children, transition.Value.Next.Waiting!.Reason);
        Assert.Equal(["child-run-1", "child-run-2"], transition.Value.Next.Waiting!.Children);

        var resolve = transition.Value.Next.Resolve("stepA", WaitOutcome.Arrived, null, now);
        Assert.NotNull(resolve);
        Assert.Equal(FlowStatus.Running, resolve.Value.Next.Status);
        Assert.Contains("stepA", resolve.Value.Next.Done);
    }
}
