using DigitalBrain.Confirmation;
using DigitalBrain.Operations;
using Xunit;

namespace DigitalBrain.Tests.Unit.Operations;

/// <summary>
/// Empirical stress-tests and invariant fuzzer for pure OperationState transitions.
/// </summary>
public sealed class OperationStateStressFacts
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    private static OperationState CreateInitial(string id = "op-stress-1") =>
        new(id, "b1", "caller", "TestPlan", "{}", null, null, OperationPhase.Running, false, 0d, null, T0, T0);

    [Fact]
    public void GeneratorFuzzer_PreservesDomainInvariantsAcrossOneThousandRandomSequences()
    {
        for (var seed = 1; seed <= 1000; seed++)
        {
            var rng = new Random(seed);
            var state = CreateInitial($"op-fuzz-{seed}");
            var now = T0;

            for (var step = 0; step < 50; step++)
            {
                now = now.AddSeconds(1);
                var action = rng.Next(0, 10);
                OperationTransition? transition = null;

                switch (action)
                {
                    case 0: // AwaitApproval
                        var planId = rng.Next(0, 2) == 0 ? "plan-valid" : (rng.Next(0, 2) == 0 ? "plan-alt" : "plan-x");
                        var guard = rng.Next(0, 2) == 0 ? "guard-" + rng.Next(1, 5) : null;
                        transition = state.AwaitApproval(planId, guard, now);
                        break;

                    case 1: // Answer Approved
                        var approvedAnswer = new ConfirmationResult(
                            rng.Next(0, 2) == 0 ? (state.ApprovalPlanId ?? "plan-valid") : "plan-wrong",
                            ConfirmationOutcome.Approved,
                            ConfirmationDenyReason.None,
                            false);
                        transition = state.Answer(approvedAnswer, now);
                        break;

                    case 2: // Answer Denied
                        var deniedAnswer = new ConfirmationResult(
                            rng.Next(0, 2) == 0 ? (state.ApprovalPlanId ?? "plan-valid") : "plan-wrong",
                            ConfirmationOutcome.Denied,
                            rng.Next(0, 2) == 0 ? ConfirmationDenyReason.ByPerson : ConfirmationDenyReason.SelfApproval,
                            false);
                        transition = state.Answer(deniedAnswer, now);
                        break;

                    case 3: // Answer Expired
                        var expiredAnswer = new ConfirmationResult(
                            rng.Next(0, 2) == 0 ? (state.ApprovalPlanId ?? "plan-valid") : "plan-wrong",
                            ConfirmationOutcome.Expired,
                            ConfirmationDenyReason.Expired,
                            false);
                        transition = state.Answer(expiredAnswer, now);
                        break;

                    case 4: // Progress Valid
                        var fraction = rng.NextDouble();
                        transition = state.Progress(fraction, now);
                        break;

                    case 5: // Progress Boundary / Extreme
                        var extreme = rng.Next(0, 3) switch
                        {
                            0 => -100.0,
                            1 => 200.0,
                            _ => double.MaxValue,
                        };
                        transition = state.Progress(extreme, now);
                        break;

                    case 6: // RequestCancel with handlerRuns: true
                        transition = state.RequestCancel(handlerRuns: true, now);
                        break;

                    case 7: // RequestCancel with handlerRuns: false
                        transition = state.RequestCancel(handlerRuns: false, now);
                        break;

                    case 8: // Complete Succeeded
                        transition = state.Complete(new OperationResult.Succeeded(), now);
                        break;

                    case 9: // Complete Failed
                        transition = state.Complete(new OperationResult.Failed("err", "Fuzz error"), now);
                        break;
                }

                if (transition is { } t)
                {
                    state = t.Next;

                    // Verify signal contract
                    Assert.NotNull(t.Signals);
                    if (state.Phase == OperationPhase.Done)
                    {
                        Assert.Contains(t.Signals, s => s is OperationDone done && done.Result == state.Result);
                    }
                    else if (state.Phase == OperationPhase.AwaitingApproval)
                    {
                        Assert.Contains(t.Signals, s => s is OperationAwaitingApproval awaiting && awaiting.PlanId == state.ApprovalPlanId);
                    }
                    else if (state.Phase == OperationPhase.Running && action is 4 or 5)
                    {
                        Assert.Contains(t.Signals, s => s is OperationProgress p && p.Fraction == state.Fraction);
                    }
                }

                // Invariant 1: If Done, Result is non-null
                if (state.Phase == OperationPhase.Done)
                {
                    Assert.NotNull(state.Result);
                }
                else
                {
                    Assert.Null(state.Result);
                }

                // Invariant 2: Fraction is strictly clamped between 0.0 and 1.0
                Assert.InRange(state.Fraction, 0d, 1d);

                // Invariant 3: If Succeeded, Fraction must be 1.0
                if (state.Result is OperationResult.Succeeded)
                {
                    Assert.Equal(1d, state.Fraction);
                }

                // Invariant 4: When AwaitingApproval, ApprovalPlanId must not be null
                if (state.Phase == OperationPhase.AwaitingApproval)
                {
                    Assert.NotNull(state.ApprovalPlanId);
                }
            }
        }
    }

    [Fact]
    public void InvalidTransitionMatrix_DoneStateRejectsAllTransitionsPermanently()
    {
        var doneSucceeded = CreateInitial().Complete(new OperationResult.Succeeded(), T0)!.Value.Next;
        var doneCancelled = CreateInitial().Complete(new OperationResult.Cancelled(), T0)!.Value.Next;
        var doneFailed = CreateInitial().Complete(new OperationResult.Failed("e", "m"), T0)!.Value.Next;

        foreach (var done in new[] { doneSucceeded, doneCancelled, doneFailed })
        {
            Assert.Equal(OperationPhase.Done, done.Phase);

            // Attempt Complete
            Assert.Null(done.Complete(new OperationResult.Succeeded(), T0.AddSeconds(1)));
            Assert.Null(done.Complete(new OperationResult.Failed("err", "msg"), T0.AddSeconds(1)));
            Assert.Null(done.Complete(new OperationResult.Cancelled(), T0.AddSeconds(1)));

            // Attempt AwaitApproval
            Assert.Null(done.AwaitApproval("plan-new", "guard", T0.AddSeconds(1)));

            // Attempt Answer
            var answer = new ConfirmationResult("plan-new", ConfirmationOutcome.Approved, ConfirmationDenyReason.None, false);
            Assert.Null(done.Answer(answer, T0.AddSeconds(1)));

            // Attempt Progress
            Assert.Null(done.Progress(0.5, T0.AddSeconds(1)));
            Assert.Null(done.Progress(1.0, T0.AddSeconds(1)));

            // Attempt Cancel
            Assert.Null(done.RequestCancel(handlerRuns: true, T0.AddSeconds(1)));
            Assert.Null(done.RequestCancel(handlerRuns: false, T0.AddSeconds(1)));
        }
    }

    [Fact]
    public void InvalidTransitionMatrix_AwaitingApprovalRejectsInvalidActions()
    {
        var awaiting = CreateInitial().AwaitApproval("plan-42", "guard-xyz", T0)!.Value.Next;
        Assert.Equal(OperationPhase.AwaitingApproval, awaiting.Phase);

        // Cannot await approval again
        Assert.Null(awaiting.AwaitApproval("plan-43", "guard-new", T0.AddSeconds(1)));

        // Cannot report progress while awaiting approval
        Assert.Null(awaiting.Progress(0.1, T0.AddSeconds(1)));
        Assert.Null(awaiting.Progress(0.9, T0.AddSeconds(1)));

        // Answering with mismatched plan ID is rejected
        var wrongPlan = new ConfirmationResult("plan-wrong", ConfirmationOutcome.Approved, ConfirmationDenyReason.None, false);
        Assert.Null(awaiting.Answer(wrongPlan, T0.AddSeconds(1)));
    }

    [Fact]
    public void InvalidTransitionMatrix_RunningWithCancelRequestedRejectsProgressAndAwaitApproval()
    {
        var cancelled = CreateInitial().RequestCancel(handlerRuns: true, T0)!.Value.Next;
        Assert.Equal(OperationPhase.Running, cancelled.Phase);
        Assert.True(cancelled.CancelRequested);

        // Cannot report progress once cancel is requested
        Assert.Null(cancelled.Progress(0.5, T0.AddSeconds(1)));

        // Cannot enter awaiting approval once cancel is requested
        Assert.Null(cancelled.AwaitApproval("plan-1", null, T0.AddSeconds(1)));

        // Subsequent RequestCancel returns null (idempotent reject)
        Assert.Null(cancelled.RequestCancel(handlerRuns: true, T0.AddSeconds(1)));

        // But Complete can finalize it
        var finalized = cancelled.Complete(new OperationResult.Cancelled(), T0.AddSeconds(2));
        Assert.NotNull(finalized);
        Assert.Equal(OperationPhase.Done, finalized.Value.Next.Phase);
        Assert.IsType<OperationResult.Cancelled>(finalized.Value.Next.Result);
    }

    [Fact]
    public void InvalidTransitionMatrix_UninitializedStateRejectsComplete()
    {
        var empty = new OperationState();
        Assert.False(empty.Exists);
        Assert.Null(empty.Complete(new OperationResult.Succeeded(), T0));
    }

    [Fact]
    public void ConcurrentTransitionRace_ConcurrentCallersConsistentlySealTerminalState()
    {
        const int iterations = 100;
        for (var i = 0; i < iterations; i++)
        {
            var current = CreateInitial($"op-race-{i}");
            var now = T0;
            var completedCount = 0;
            var cancelledCount = 0;

            Parallel.For(0, 40, taskIdx =>
            {
                if (taskIdx < 10)
                {
                    var t = current.Complete(new OperationResult.Succeeded(), now.AddSeconds(taskIdx));
                    if (t is not null && Interlocked.CompareExchange(ref current, t.Value.Next, current) == current)
                    {
                        Interlocked.Increment(ref completedCount);
                    }
                }
                else if (taskIdx < 20)
                {
                    var t = current.RequestCancel(handlerRuns: false, now.AddSeconds(taskIdx));
                    if (t is not null && Interlocked.CompareExchange(ref current, t.Value.Next, current) == current)
                    {
                        Interlocked.Increment(ref cancelledCount);
                    }
                }
                else
                {
                    var t = current.Progress(0.1 * (taskIdx - 20), now.AddSeconds(taskIdx));
                    if (t is not null)
                    {
                        Interlocked.CompareExchange(ref current, t.Value.Next, current);
                    }
                }
            });

            // Once any terminal transition wins, state must be Done and Result must not be null
            if (current.Phase == OperationPhase.Done)
            {
                Assert.NotNull(current.Result);
                // No further transition can succeed
                Assert.Null(current.Complete(new OperationResult.Succeeded(), now.AddDays(1)));
                Assert.Null(current.Progress(0.5, now.AddDays(1)));
            }
        }
    }
}
