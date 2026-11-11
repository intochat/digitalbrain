using DigitalBrain.Confirmation;
using DigitalBrain.Neurons;
using DigitalBrain.Operations;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests.Unit.Operations;

public sealed class OperationNeuronFacts
{
    [Fact]
    public async Task BeginInitializesStateAndPublishesStartedSignal()
    {
        var context = new FakeNeuronContext(new NeuronId(new BrainId("b1"), "runtime.operation", "op-1"));
        var neuron = new OperationNeuron();
        NeuronAttachment.Attach(neuron, context);

        var initial = new OperationState("op-1", "b1", "origin", "TestPlan", "{}", null, null,
            OperationPhase.Running, false, 0d, null, context.Time.GetUtcNow(), context.Time.GetUtcNow());

        var begun = await neuron.Begin(initial);
        Assert.Equal("op-1", begun.Id);
        Assert.Equal(OperationPhase.Running, begun.Phase);

        var started = Assert.Single(context.Published.OfType<OperationStarted>());
        Assert.Equal(new OperationId("op-1"), started.Operation);
        Assert.Equal("TestPlan", started.Kind);
        Assert.Equal("origin", started.Origin);
    }

    [Fact]
    public async Task BeginRejectsMismatchedId()
    {
        var context = new FakeNeuronContext(new NeuronId(new BrainId("b1"), "runtime.operation", "op-1"));
        var neuron = new OperationNeuron();
        NeuronAttachment.Attach(neuron, context);

        var initial = new OperationState("op-mismatched", "b1", "origin", "TestPlan", "{}", null, null,
            OperationPhase.Running, false, 0d, null, context.Time.GetUtcNow(), context.Time.GetUtcNow());

        await Assert.ThrowsAsync<ArgumentException>(() => neuron.Begin(initial));
    }

    [Fact]
    public async Task BeginWhenAlreadyExistsReturnsExistingStateWithoutDuplicateSignals()
    {
        var context = new FakeNeuronContext(new NeuronId(new BrainId("b1"), "runtime.operation", "op-1"));
        var neuron = new OperationNeuron();
        NeuronAttachment.Attach(neuron, context);

        var initial = new OperationState("op-1", "b1", "origin", "TestPlan", "{}", null, null,
            OperationPhase.Running, false, 0d, null, context.Time.GetUtcNow(), context.Time.GetUtcNow());

        var first = await neuron.Begin(initial);
        var second = await neuron.Begin(initial);

        Assert.Equal(first.Id, second.Id);
        Assert.Single(context.Published.OfType<OperationStarted>());
    }

    [Fact]
    public async Task PreparedWithApprovalEntersAwaitingApproval()
    {
        var context = new FakeNeuronContext(new NeuronId(new BrainId("b1"), "runtime.operation", "op-1"));
        var neuron = new OperationNeuron();
        NeuronAttachment.Attach(neuron, context);

        var initial = new OperationState("op-1", "b1", "origin", "TestPlan", "{}", null, null,
            OperationPhase.Running, false, 0d, null, context.Time.GetUtcNow(), context.Time.GetUtcNow());
        await neuron.Begin(initial);

        var question = new ConfirmationPlan("cf-1", ConfirmationAction.Delete, true, "Title", "Body", "Yes", "No", "Target");
        await neuron.Prepared(new OperationApproval(question, "guard-1"));

        var state = (await neuron.Get())!;
        Assert.Equal(OperationPhase.AwaitingApproval, state.Phase);
        Assert.Equal("cf-1", state.ApprovalPlanId);
        Assert.Equal("guard-1", state.Guard);

        Assert.Contains(context.Published, s => s is OperationAwaitingApproval awaiting && awaiting.PlanId == "cf-1");
    }

    [Fact]
    public async Task OnResolvedIgnoresMismatchedPlanOrNonAwaitingPhase()
    {
        var context = new FakeNeuronContext(new NeuronId(new BrainId("b1"), "runtime.operation", "op-1"));
        var neuron = new OperationNeuron();
        NeuronAttachment.Attach(neuron, context);

        var initial = new OperationState("op-1", "b1", "origin", "TestPlan", "{}", null, null,
            OperationPhase.Running, false, 0d, null, context.Time.GetUtcNow(), context.Time.GetUtcNow());
        await neuron.Begin(initial);

        var resolved = new ConfirmationResolved(
            "cf-1", ConfirmationAction.Delete, "target",
            ConfirmationPrincipal.App("requester"),
            null, ConfirmationPolicy.Always, ConfirmationOutcome.Approved, ConfirmationDenyReason.None, false, context.Time.GetUtcNow());

        // Phase is Running, not AwaitingApproval -> OnResolved must be a no-op
        await neuron.OnResolved(resolved);
        var state = (await neuron.Get())!;
        Assert.Equal(OperationPhase.Running, state.Phase);

        // Enter AwaitingApproval for cf-1
        var question = new ConfirmationPlan("cf-1", ConfirmationAction.Delete, true, "Title", "Body", "Yes", "No", "Target");
        await neuron.Prepared(new OperationApproval(question, "guard-1"));

        // Receive OnResolved for cf-different -> must be a no-op
        var mismatchedResolved = resolved with { PlanId = "cf-different" };
        await neuron.OnResolved(mismatchedResolved);

        var stateStillAwaiting = (await neuron.Get())!;
        Assert.Equal(OperationPhase.AwaitingApproval, stateStillAwaiting.Phase);
        Assert.Equal("cf-1", stateStillAwaiting.ApprovalPlanId);
    }

    [Fact]
    public async Task ProgressUpdatesFractionAndPublishesSignal()
    {
        var context = new FakeNeuronContext(new NeuronId(new BrainId("b1"), "runtime.operation", "op-1"));
        var neuron = new OperationNeuron();
        NeuronAttachment.Attach(neuron, context);

        var initial = new OperationState("op-1", "b1", "origin", "TestPlan", "{}", null, null,
            OperationPhase.Running, false, 0d, null, context.Time.GetUtcNow(), context.Time.GetUtcNow());
        await neuron.Begin(initial);

        Assert.True(await neuron.Progress(0.5));
        var state = (await neuron.Get())!;
        Assert.Equal(0.5, state.Fraction);

        var progress = Assert.Single(context.Published.OfType<OperationProgress>());
        Assert.Equal(0.5, progress.Fraction);
    }

    [Fact]
    public async Task CompleteEndsOperationWithResult()
    {
        var context = new FakeNeuronContext(new NeuronId(new BrainId("b1"), "runtime.operation", "op-1"));
        var neuron = new OperationNeuron();
        NeuronAttachment.Attach(neuron, context);

        var initial = new OperationState("op-1", "b1", "origin", "TestPlan", "{}", null, null,
            OperationPhase.Running, false, 0d, null, context.Time.GetUtcNow(), context.Time.GetUtcNow());
        await neuron.Begin(initial);

        var completed = await neuron.Complete(new OperationResult.Succeeded());
        Assert.Equal(OperationPhase.Done, completed.Phase);
        Assert.IsType<OperationResult.Succeeded>(completed.Result);

        var done = Assert.Single(context.Published.OfType<OperationDone>());
        Assert.IsType<OperationResult.Succeeded>(done.Result);

        // Second Complete call returns existing state without new signals
        var completedAgain = await neuron.Complete(new OperationResult.Failed("err", "ignored"));
        Assert.Equal(OperationPhase.Done, completedAgain.Phase);
        Assert.IsType<OperationResult.Succeeded>(completedAgain.Result);
        Assert.Single(context.Published.OfType<OperationDone>());
    }

    [Fact]
    public async Task RequestCancelMarksCancelledAndCancelsFutureProgress()
    {
        var context = new FakeNeuronContext(new NeuronId(new BrainId("b1"), "runtime.operation", "op-1"));
        var neuron = new OperationNeuron();
        NeuronAttachment.Attach(neuron, context);

        var initial = new OperationState("op-1", "b1", "origin", "TestPlan", "{}", null, null,
            OperationPhase.Running, false, 0d, null, context.Time.GetUtcNow(), context.Time.GetUtcNow());
        await neuron.Begin(initial);

        Assert.True(await neuron.RequestCancel());
        var state = (await neuron.Get())!;
        Assert.Equal(OperationPhase.Done, state.Phase);
        Assert.IsType<OperationResult.Cancelled>(state.Result);

        Assert.False(await neuron.Progress(0.8));
    }
}
