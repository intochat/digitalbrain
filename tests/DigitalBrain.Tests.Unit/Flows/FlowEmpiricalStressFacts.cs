using System.Collections.Concurrent;
using DigitalBrain.Confirmation;
using DigitalBrain.Contracts;
using DigitalBrain.Flows;
using DigitalBrain.Neurons;
using DigitalBrain.Tests.Unit.Neurons;
using DigitalBrain.Units;
using Xunit;

namespace DigitalBrain.Tests.Unit.Flows;

public enum StressKind
{
    FastThreeStep,
    ApprovalWait,
    CancelMidStep,
    TransientInterruption
}

public sealed class StressState
{
    public StressKind Kind { get; init; }
    public int InterruptionsInjected { get; set; }
    public int StepsExecuted { get; set; }
    public string? PlanId { get; set; }
}

[Neuron("stress.flow")]
public sealed class ConcurrentStressFlow : Flow<StressState>
{
    public TaskCompletionSource HangEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool WasCancelled { get; private set; }

    public FlowState<StressState> Current => State;

    protected override IReadOnlyList<string> Steps => ["step1", "step2", "step3"];

    protected override async Task<StepResult<StressState>> Run(string step, StressState inner, CancellationToken ct)
    {
        inner.StepsExecuted++;

        if (inner.Kind == StressKind.CancelMidStep && step == "step2")
        {
            HangEntered.TrySetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            catch (OperationCanceledException)
            {
                WasCancelled = true;
                throw;
            }
        }

        if (inner.Kind == StressKind.TransientInterruption && step == "step1" && inner.InterruptionsInjected < 2)
        {
            inner.InterruptionsInjected++;
            using var foreign = new CancellationTokenSource();
            await foreign.CancelAsync();
            foreign.Token.ThrowIfCancellationRequested();
        }

        if (inner.Kind == StressKind.ApprovalWait && step == "step2")
        {
            return new StepResult<StressState>(inner, new Waiting("step2", WaitReason.Approval, null, null));
        }

        return new StepResult<StressState>(inner);
    }

    protected override ConfirmationPlan PlanFor(string step, StressState inner)
        => base.PlanFor(step, inner) with { Id = inner.PlanId ?? Guid.NewGuid().ToString("N") };

    public Task Deliver(ConfirmationResolved resolved) => OnConfirmation(resolved);

    public Task Tick(string name) => NeuronAttachment.Remind(this, name, CancellationToken.None);
}

public sealed class FlowEmpiricalStressFacts
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ConfirmationResolved Resolution(string planId, ConfirmationOutcome outcome) => new(
        planId,
        ConfirmationAction.Other,
        "f",
        ConfirmationPrincipal.App("stress.flow"),
        outcome == ConfirmationOutcome.Approved ? ConfirmationPrincipal.Person("admin") : null,
        ConfirmationPolicy.DestructiveOnly,
        outcome,
        outcome == ConfirmationOutcome.Approved ? ConfirmationDenyReason.None : ConfirmationDenyReason.ByPerson,
        false,
        DateTimeOffset.UnixEpoch);

    private static async Task Drain(RecordingContext context)
    {
        while (context.Deferred.Count > 0)
        {
            var work = context.Deferred[0];
            context.Deferred.RemoveAt(0);
            await work();
        }
    }

    // -----------------------------------------------------------------------------------------
    // 1. Stress-test Step Interruptions & Exact Failure at 20th Interruption
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void StepInterruptions_PureStateTransition_IncrementsUpTo19AndFailsAt20()
    {
        var now = new DateTimeOffset(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
        var initial = new FlowState<StressState>();
        var started = initial.Start("Stress", "user", null, new StressState(), now, "trace-1", "flow");
        Assert.NotNull(started);
        var state = started.Value.Next;

        var startStep = state.StartStep(["step1"], now);
        Assert.NotNull(startStep);
        state = startStep.Value.Next;
        Assert.Equal("step1", state.Current);
        Assert.Equal(0, state.Interruptions);

        // Interrupt 19 times -> stays running, step remains "step1", interruptions counter increments
        for (var i = 1; i <= 19; i++)
        {
            var trans = state.Interrupted("step1", $"error-{i}", now, maxInterruptions: 20);
            Assert.NotNull(trans);
            state = trans.Value.Next;
            Assert.Equal(FlowStatus.Running, state.Status);
            Assert.Equal("step1", state.Current);
            Assert.Equal(i, state.Interruptions);
            var stepSignal = Assert.Single(trans.Value.Signals.OfType<FlowStep>());
            Assert.Equal(StepOutcome.Interrupted, stepSignal.Outcome);
            Assert.Equal($"error-{i}", stepSignal.Detail);
        }

        // 20th interruption -> exact failure transition
        var failTrans = state.Interrupted("step1", "error-20", now, maxInterruptions: 20);
        Assert.NotNull(failTrans);
        var failed = failTrans.Value.Next;
        Assert.Equal(FlowStatus.Failed, failed.Status);
        Assert.Equal(20, failed.Interruptions);
        Assert.Null(failed.Current);
        Assert.Equal(now, failed.Ended);

        var stepFailed = Assert.Single(failTrans.Value.Signals.OfType<FlowStep>());
        Assert.Equal(StepOutcome.Failed, stepFailed.Outcome);
        Assert.Equal("interrupted 20 times", stepFailed.Detail);

        var flowFailed = Assert.Single(failTrans.Value.Signals.OfType<FlowDone>());
        Assert.Equal(FlowOutcome.Failed, flowFailed.Outcome);
        Assert.Equal("interrupted 20 times", flowFailed.Detail);
    }

    [Fact]
    public void StepInterruptions_ResetToZeroOnStepCompletion()
    {
        var now = new DateTimeOffset(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
        var state = new FlowState<StressState>().Start("Stress", "user", null, new StressState(), now, "trace-1", "flow")!.Value.Next;
        state = state.StartStep(["step1", "step2"], now)!.Value.Next;

        // Interrupt step 1 five times
        for (var i = 1; i <= 5; i++)
        {
            state = state.Interrupted("step1", $"err-{i}", now)!.Value.Next;
        }
        Assert.Equal(5, state.Interruptions);

        // Step 1 completes -> Interruptions counter resets to 0
        state = state.Complete("step1", new StepResult<StressState>(new StressState()))!.Value.Next;
        Assert.Equal(0, state.Interruptions);
        Assert.Contains("step1", state.Done);
        Assert.Null(state.Current);

        // Step 2 starts
        state = state.StartStep(["step1", "step2"], now)!.Value.Next;
        Assert.Equal("step2", state.Current);
        Assert.Equal(0, state.Interruptions);
    }

    // -----------------------------------------------------------------------------------------
    // 2. Stress-test Cancellation Mid-Step
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task CancelMidStep_AbortsRunningBodyAndTransitionsToCancelled()
    {
        var id = new NeuronId(new BrainId("b1"), "stress.flow", "cancel-mid-test");
        var reminders = new RecordingReminders();
        var confirmations = new RecordingConfirmations();
        var context = new RecordingContext(id) { RecordedConfirmations = confirmations, RecordedReminders = reminders };
        context.Neurons[IConfirmationRequests.Of(id.Brain)] = confirmations;

        var flow = new ConcurrentStressFlow();
        NeuronAttachment.Attach(flow, context);
        context.Neurons[id] = flow;
        await NeuronAttachment.Activate(flow, Ct);

        // Start flow with CancelMidStep kind (step 1 finishes, step 2 hangs)
        await flow.Start("CancelTest", "alice", null, new StressState { Kind = StressKind.CancelMidStep });
        var advancing = Drain(context);

        // Wait until step 2 is actively executing in Run
        await flow.HangEntered.Task;
        Assert.Equal("step2", flow.Current.Current);
        Assert.Equal(FlowStatus.Running, flow.Current.Status);

        // Cancel mid-step
        await flow.Cancel("user-cancelled-mid-flight");
        await advancing;
        await Drain(context);

        // Verify body token was cancelled
        Assert.True(flow.WasCancelled);

        // Verify state is Cancelled
        Assert.Equal(FlowStatus.Cancelled, flow.Current.Status);
        Assert.Null(flow.Current.Current);
        Assert.NotNull(flow.Current.Ended);

        // Verify step 1 is done, step 2 was dropped (never saved as done)
        Assert.Equal(["step1"], flow.Current.Done);

        // Verify FlowDone signal was emitted with Cancelled outcome
        var doneSignal = context.Writes.SelectMany(w => w.Signals).OfType<FlowDone>().Single();
        Assert.Equal(FlowOutcome.Cancelled, doneSignal.Outcome);
        Assert.Equal("user-cancelled-mid-flight", doneSignal.Detail);
    }

    // -----------------------------------------------------------------------------------------
    // 3. Stress-test High Concurrency across 100 Flows
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task HighConcurrency_100ConcurrentFlows_ExecuteDeterministicallyWithoutRaceConditions()
    {
        const int flowCount = 100;
        var brain = new BrainId("stress-brain");

        var completedFlows = new ConcurrentBag<string>();
        var cancelledFlows = new ConcurrentBag<string>();
        var errors = new ConcurrentBag<Exception>();

        // Launch 100 concurrent flows simultaneously
        var tasks = Enumerable.Range(0, flowCount).Select(async i =>
        {
            try
            {
                var flowId = new NeuronId(brain, "stress.flow", $"flow-{i:D3}");
                var kind = (i % 4) switch
                {
                    0 => StressKind.FastThreeStep,
                    1 => StressKind.ApprovalWait,
                    2 => StressKind.CancelMidStep,
                    _ => StressKind.TransientInterruption
                };

                var reminders = new RecordingReminders();
                var confirmations = new RecordingConfirmations();
                var context = new RecordingContext(flowId) { RecordedConfirmations = confirmations, RecordedReminders = reminders };
                context.Neurons[IConfirmationRequests.Of(brain)] = confirmations;

                var flow = new ConcurrentStressFlow();
                NeuronAttachment.Attach(flow, context);
                context.Neurons[flowId] = flow;
                await NeuronAttachment.Activate(flow, Ct);

                var state = new StressState { Kind = kind, PlanId = $"plan-{i:D3}" };

                switch (kind)
                {
                    case StressKind.FastThreeStep:
                        {
                            await flow.Start($"Flow-{i}", "user", null, state);
                            await Drain(context);

                            Assert.Equal(FlowStatus.Completed, flow.Current.Status);
                            Assert.Equal(["step1", "step2", "step3"], flow.Current.Done);
                            completedFlows.Add(flowId.ToString());
                            break;
                        }

                    case StressKind.ApprovalWait:
                        {
                            await flow.Start($"Flow-{i}", "user", null, state);
                            await Drain(context);

                            Assert.Equal(FlowStatus.Waiting, flow.Current.Status);
                            Assert.Equal("step2", flow.Current.Waiting?.Step);

                            // Resolve approval
                            confirmations.Answer($"plan-{i:D3}", ConfirmationOutcome.Approved);
                            await flow.Deliver(Resolution($"plan-{i:D3}", ConfirmationOutcome.Approved));
                            await Drain(context);

                            Assert.Equal(FlowStatus.Completed, flow.Current.Status);
                            Assert.Equal(["step1", "step2", "step3"], flow.Current.Done);
                            completedFlows.Add(flowId.ToString());
                            break;
                        }

                    case StressKind.CancelMidStep:
                        {
                            await flow.Start($"Flow-{i}", "user", null, state);
                            var advancing = Drain(context);
                            await flow.HangEntered.Task;

                            await flow.Cancel($"cancel-{i}");
                            await advancing;
                            await Drain(context);

                            Assert.Equal(FlowStatus.Cancelled, flow.Current.Status);
                            Assert.True(flow.WasCancelled);
                            cancelledFlows.Add(flowId.ToString());
                            break;
                        }

                    case StressKind.TransientInterruption:
                        {
                            await flow.Start($"Flow-{i}", "user", null, state);
                            await Drain(context);

                            // The flow got interrupted on step 1 (interruption 1)
                            Assert.Equal(FlowStatus.Running, flow.Current.Status);
                            Assert.Equal(1, flow.Current.Interruptions);

                            // Nudge 1: triggers second interruption
                            await flow.Tick("flow-nudge");
                            await Drain(context);
                            Assert.Equal(2, flow.Current.Interruptions);

                            // Nudge 2: runs step 1 cleanly, then advances through step 2 and step 3
                            await flow.Tick("flow-nudge");
                            await Drain(context);

                            Assert.Equal(FlowStatus.Completed, flow.Current.Status);
                            Assert.Equal(0, flow.Current.Interruptions);
                            Assert.Equal(["step1", "step2", "step3"], flow.Current.Done);
                            completedFlows.Add(flowId.ToString());
                            break;
                        }
                }
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
        });

        await Task.WhenAll(tasks);

        // Verification across all 100 concurrent executions
        Assert.Empty(errors);
        Assert.Equal(75, completedFlows.Count);
        Assert.Equal(25, cancelledFlows.Count);
        Assert.Equal(100, completedFlows.Count + cancelledFlows.Count);
    }
}
