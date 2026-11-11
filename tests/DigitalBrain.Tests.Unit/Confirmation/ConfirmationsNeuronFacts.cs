using DigitalBrain.Confirmation;
using DigitalBrain.Neurons;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Tests.Unit.Confirmation;

public sealed class ConfirmationsNeuronFacts
{
    private static ConfirmationPlan Plan(string id = "cf-1") =>
        new(id, ConfirmationAction.Delete, true, "Title", "Body", "Yes", "No", "Target")
        {
            Expiry = TimeSpan.FromMinutes(2)
        };

    [Fact]
    public async Task RequestAndOutcomeWorkflow()
    {
        var context = new FakeNeuronContext(new NeuronId(new BrainId("b1"), "runtime.confirmations", "confirmations"));
        context.Caller = new DigitalBrain.Access.Caller(new DigitalBrain.Access.Actor.Person("alice", "acc-1"), new BrainId("b1"));
        var neuron = new ConfirmationsNeuron();
        NeuronAttachment.Attach(neuron, context);

        var plan = Plan("cf-workflow");
        var result = await neuron.Request(plan);
        Assert.Null(result); // Pending human approval

        var inbox = await neuron.Read();
        Assert.Single(inbox.Pending);
        Assert.Equal("cf-workflow", inbox.Pending[0].Plan.Id);

        // Authoritative outcome check while pending returns null
        Assert.Null(await neuron.Outcome("cf-workflow"));

        // Resolve approve
        await neuron.Resolve("cf-workflow", approve: true);

        var outcome = await neuron.Outcome("cf-workflow");
        Assert.NotNull(outcome);
        Assert.Equal(ConfirmationOutcome.Approved, outcome.Outcome);
    }

    [Fact]
    public async Task WithdrawCancelsPendingRequest()
    {
        var context = new FakeNeuronContext(new NeuronId(new BrainId("b1"), "runtime.confirmations", "confirmations"));
        var neuron = new ConfirmationsNeuron();
        NeuronAttachment.Attach(neuron, context);

        var plan = Plan("cf-withdraw");
        await neuron.Request(plan);

        await neuron.Withdraw("cf-withdraw");

        var outcome = await neuron.Outcome("cf-withdraw");
        Assert.NotNull(outcome);
        Assert.Equal(ConfirmationOutcome.Denied, outcome.Outcome);
        Assert.Equal(ConfirmationDenyReason.PlanInvalid, outcome.Reason);
    }
}
