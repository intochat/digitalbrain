using DigitalBrain.Confirmation;
using DigitalBrain.Operations;
using Xunit;

namespace DigitalBrain.Tests.Unit.Operations;

public sealed class OperationStateFacts
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T1 = T0.AddSeconds(10);
    private static readonly DateTimeOffset T2 = T0.AddSeconds(20);

    private static OperationState Initial(string id = "op-1") =>
        new(id, "b1", "caller", "TestPlan", "{}", null, null, OperationPhase.Running, false, 0d, null, T0, T0);

    [Fact]
    public void AwaitApprovalTransitionsRunningToAwaiting()
    {
        var state = Initial();
        var transition = state.AwaitApproval("plan-1", "guard-abc", T1);

        Assert.NotNull(transition);
        Assert.Equal(OperationPhase.AwaitingApproval, transition.Value.Next.Phase);
        Assert.Equal("plan-1", transition.Value.Next.ApprovalPlanId);
        Assert.Equal("guard-abc", transition.Value.Next.Guard);
        Assert.Equal(T1, transition.Value.Next.Updated);

        var signal = Assert.Single(transition.Value.Signals);
        var awaiting = Assert.IsType<OperationAwaitingApproval>(signal);
        Assert.Equal(new OperationId("op-1"), awaiting.Operation);
        Assert.Equal("plan-1", awaiting.PlanId);
    }

    [Fact]
    public void AwaitApprovalRejectsIfAlreadyAwaitingOrCancelRequestedOrDone()
    {
        var state = Initial();
        var awaiting = state.AwaitApproval("plan-1", null, T1)!.Value.Next;
        Assert.Null(awaiting.AwaitApproval("plan-2", null, T2));

        var cancelled = state with { CancelRequested = true };
        Assert.Null(cancelled.AwaitApproval("plan-1", null, T1));

        var done = state with { Phase = OperationPhase.Done };
        Assert.Null(done.AwaitApproval("plan-1", null, T1));
    }

    [Fact]
    public void AnswerApprovedResumesOperation()
    {
        var state = Initial();
        var awaiting = state.AwaitApproval("plan-1", null, T1)!.Value.Next;

        var answer = new ConfirmationResult("plan-1", ConfirmationOutcome.Approved, ConfirmationDenyReason.None, false);
        var transition = awaiting.Answer(answer, T2);

        Assert.NotNull(transition);
        Assert.Equal(OperationPhase.Running, transition.Value.Next.Phase);
        Assert.Equal(T2, transition.Value.Next.Resumed);
        Assert.Equal(T2, transition.Value.Next.Updated);

        var signal = Assert.Single(transition.Value.Signals);
        var progress = Assert.IsType<OperationProgress>(signal);
        Assert.Equal(0d, progress.Fraction);
    }

    [Fact]
    public void AnswerDeniedCompletesOperationAsFailed()
    {
        var state = Initial();
        var awaiting = state.AwaitApproval("plan-1", null, T1)!.Value.Next;

        var answer = new ConfirmationResult("plan-1", ConfirmationOutcome.Denied, ConfirmationDenyReason.ByPerson, false);
        var transition = awaiting.Answer(answer, T2);

        Assert.NotNull(transition);
        Assert.Equal(OperationPhase.Done, transition.Value.Next.Phase);
        var failed = Assert.IsType<OperationResult.Failed>(transition.Value.Next.Result);
        Assert.Equal("declined", failed.Code);

        var signal = Assert.Single(transition.Value.Signals);
        Assert.IsType<OperationDone>(signal);
    }

    [Fact]
    public void AnswerExpiredCompletesOperationAsExpired()
    {
        var state = Initial();
        var awaiting = state.AwaitApproval("plan-1", null, T1)!.Value.Next;

        var answer = new ConfirmationResult("plan-1", ConfirmationOutcome.Expired, ConfirmationDenyReason.Expired, false);
        var transition = awaiting.Answer(answer, T2);

        Assert.NotNull(transition);
        Assert.Equal(OperationPhase.Done, transition.Value.Next.Phase);
        var failed = Assert.IsType<OperationResult.Failed>(transition.Value.Next.Result);
        Assert.Equal("expired", failed.Code);
    }

    [Fact]
    public void AnswerSelfApprovalCompletesOperationAsOnlyPersonCanApprove()
    {
        var state = Initial();
        var awaiting = state.AwaitApproval("plan-1", null, T1)!.Value.Next;

        var answer = new ConfirmationResult("plan-1", ConfirmationOutcome.Denied, ConfirmationDenyReason.SelfApproval, false);
        var transition = awaiting.Answer(answer, T2);

        Assert.NotNull(transition);
        var failed = Assert.IsType<OperationResult.Failed>(transition.Value.Next.Result);
        Assert.Equal("declined", failed.Code);
        Assert.Equal("Only a person can approve this.", failed.Message);
    }

    [Fact]
    public void AnswerRejectsMismatchedPlanOrNonAwaitingPhase()
    {
        var state = Initial();
        var awaiting = state.AwaitApproval("plan-1", null, T1)!.Value.Next;

        var mismatched = new ConfirmationResult("plan-wrong", ConfirmationOutcome.Approved, ConfirmationDenyReason.None, false);
        Assert.Null(awaiting.Answer(mismatched, T2));

        var running = Initial();
        var answer = new ConfirmationResult("plan-1", ConfirmationOutcome.Approved, ConfirmationDenyReason.None, false);
        Assert.Null(running.Answer(answer, T2));
    }

    [Fact]
    public void RequestCancelWithHandlerRunsSetsCancelRequested()
    {
        var state = Initial();
        var transition = state.RequestCancel(handlerRuns: true, T1);

        Assert.NotNull(transition);
        Assert.True(transition.Value.Next.CancelRequested);
        Assert.Equal(OperationPhase.Running, transition.Value.Next.Phase);
        Assert.Empty(transition.Value.Signals);
    }

    [Fact]
    public void RequestCancelWithoutHandlerRunsCompletesImmediately()
    {
        var state = Initial();
        var transition = state.RequestCancel(handlerRuns: false, T1);

        Assert.NotNull(transition);
        Assert.Equal(OperationPhase.Done, transition.Value.Next.Phase);
        Assert.IsType<OperationResult.Cancelled>(transition.Value.Next.Result);

        var signal = Assert.Single(transition.Value.Signals);
        Assert.IsType<OperationDone>(signal);
    }

    [Fact]
    public void RequestCancelWhileAwaitingApprovalCompletesImmediately()
    {
        var state = Initial();
        var awaiting = state.AwaitApproval("plan-1", null, T1)!.Value.Next;

        var transition = awaiting.RequestCancel(handlerRuns: true, T2);

        Assert.NotNull(transition);
        Assert.Equal(OperationPhase.Done, transition.Value.Next.Phase);
        Assert.IsType<OperationResult.Cancelled>(transition.Value.Next.Result);
    }

    [Fact]
    public void CompleteSucceededSetsFractionTo1AndPublishesDone()
    {
        var state = Initial() with { Fraction = 0.5 };
        var transition = state.Complete(new OperationResult.Succeeded(), T1);

        Assert.NotNull(transition);
        Assert.Equal(OperationPhase.Done, transition.Value.Next.Phase);
        Assert.Equal(1d, transition.Value.Next.Fraction);
        Assert.IsType<OperationResult.Succeeded>(transition.Value.Next.Result);

        var signal = Assert.Single(transition.Value.Signals);
        var done = Assert.IsType<OperationDone>(signal);
        Assert.IsType<OperationResult.Succeeded>(done.Result);
    }

    [Fact]
    public void CompleteRejectsAlreadyDoneOrNonExistent()
    {
        var done = Initial() with { Phase = OperationPhase.Done };
        Assert.Null(done.Complete(new OperationResult.Succeeded(), T1));

        var empty = new OperationState();
        Assert.Null(empty.Complete(new OperationResult.Succeeded(), T1));
    }

    [Fact]
    public void ProgressClampsFractionAndPublishesSignal()
    {
        var state = Initial();
        var t1 = state.Progress(0.75, T1);

        Assert.NotNull(t1);
        Assert.Equal(0.75, t1.Value.Next.Fraction);
        Assert.Equal(0.75, Assert.IsType<OperationProgress>(Assert.Single(t1.Value.Signals)).Fraction);

        var tClampedHigh = state.Progress(1.5, T1);
        Assert.NotNull(tClampedHigh);
        Assert.Equal(1d, tClampedHigh.Value.Next.Fraction);

        var tClampedLow = state.Progress(-0.5, T1);
        Assert.NotNull(tClampedLow);
        Assert.Equal(0d, tClampedLow.Value.Next.Fraction);
    }

    [Fact]
    public void ProgressRejectsWhenCancelRequestedOrNotRunning()
    {
        var cancelled = Initial() with { CancelRequested = true };
        Assert.Null(cancelled.Progress(0.5, T1));

        var awaiting = Initial().AwaitApproval("plan-1", null, T1)!.Value.Next;
        Assert.Null(awaiting.Progress(0.5, T2));

        var done = Initial() with { Phase = OperationPhase.Done };
        Assert.Null(done.Progress(0.5, T1));
    }
}
