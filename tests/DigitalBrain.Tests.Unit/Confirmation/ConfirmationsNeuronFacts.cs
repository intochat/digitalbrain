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

    [Fact]
    public async Task Resolve_RequiresPerson_RejectsNullAndAgentCallersWithoutPoisoningPendingPlan_ThenPersonSucceeds()
    {
        var brain = new BrainId("b1");
        var context = new FakeNeuronContext(new NeuronId(brain, "runtime.confirmations", "confirmations"));
        var raiserNeuron = new NeuronId(brain, "flow.feature-pr", "run-1");
        var alice = new DigitalBrain.Access.Actor.Person("alice", "acc-1");
        context.Caller = new DigitalBrain.Access.Caller(alice, brain, via: raiserNeuron);

        var neuron = new ConfirmationsNeuron();
        NeuronAttachment.Attach(neuron, context);

        var plan = Plan("cf-pr-human") with
        {
            RequiresPerson = true,
            Kind = "pr-approval",
            Subject = "repo#42",
            Expiry = TimeSpan.FromHours(48),
        };

        Assert.Null(await neuron.Request(plan));

        // 1. Unstamped / null caller MUST NOT count as a person and MUST NOT cancel/poison the pending plan
        context.Caller = null;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => neuron.Resolve("cf-pr-human", approve: true));
        Assert.Null(await neuron.Outcome("cf-pr-human"));
        Assert.Single((await neuron.Read()).Pending);

        // 2. Agent caller MUST throw UnauthorizedAccessException and leave plan Pending
        context.Caller = new DigitalBrain.Access.Caller(new DigitalBrain.Access.Actor.Agent(alice), brain);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => neuron.Resolve("cf-pr-human", approve: true));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => neuron.Resolve("cf-pr-human", approve: false));
        Assert.Null(await neuron.Outcome("cf-pr-human"));
        Assert.Single((await neuron.Read()).Pending);

        // 3. Relayed person caller (via != null or conversation != null) MUST also throw and leave plan Pending
        context.Caller = new DigitalBrain.Access.Caller(alice, brain, via: raiserNeuron);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => neuron.Resolve("cf-pr-human", approve: true));
        context.Caller = new DigitalBrain.Access.Caller(alice, brain, conversation: "conv-1");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => neuron.Resolve("cf-pr-human", approve: true));
        Assert.Null(await neuron.Outcome("cf-pr-human"));

        // 4. Direct person caller without conversation succeeds and approves the preserved 48h plan
        context.Caller = new DigitalBrain.Access.Caller(alice, brain);
        var inbox = await neuron.Read();
        var pending = Assert.Single(inbox.Pending);
        Assert.Equal(TimeSpan.FromHours(48), pending.Plan.Expiry);

        await neuron.Resolve("cf-pr-human", approve: true);

        var outcome = await neuron.Outcome("cf-pr-human");
        Assert.NotNull(outcome);
        Assert.Equal(ConfirmationOutcome.Approved, outcome.Outcome);
        Assert.Empty((await neuron.Read()).Pending);
    }

    [Fact]
    public async Task Withdraw_OnlyRaiserOrDirectPersonMayWithdraw()
    {
        var brain = new BrainId("b1");
        var context = new FakeNeuronContext(new NeuronId(brain, "runtime.confirmations", "confirmations"));
        var raiserNeuron = new NeuronId(brain, "flow.feature-pr", "run-1");
        var otherNeuron = new NeuronId(brain, "app.rogue", "main");
        var alice = new DigitalBrain.Access.Actor.Person("alice", "acc-1");
        context.Caller = new DigitalBrain.Access.Caller(alice, brain, via: raiserNeuron);

        var neuron = new ConfirmationsNeuron();
        NeuronAttachment.Attach(neuron, context);

        var plan = Plan("cf-withdraw-guard") with { RequiresPerson = true };
        Assert.Null(await neuron.Request(plan));

        // Unrelated neuron/agent cannot withdraw
        context.Caller = new DigitalBrain.Access.Caller(alice, brain, via: otherNeuron);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => neuron.Withdraw("cf-withdraw-guard"));

        context.Caller = new DigitalBrain.Access.Caller(new DigitalBrain.Access.Actor.Agent(alice), brain);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => neuron.Withdraw("cf-withdraw-guard"));

        context.Caller = null;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => neuron.Withdraw("cf-withdraw-guard"));
        Assert.Null(await neuron.Outcome("cf-withdraw-guard"));

        // Original raiser CAN withdraw
        context.Caller = new DigitalBrain.Access.Caller(alice, brain, via: raiserNeuron);
        await neuron.Withdraw("cf-withdraw-guard");

        var outcome = await neuron.Outcome("cf-withdraw-guard");
        Assert.NotNull(outcome);
        Assert.Equal(ConfirmationOutcome.Denied, outcome.Outcome);
        Assert.Equal(ConfirmationDenyReason.PlanInvalid, outcome.Reason);
    }

    [Fact]
    public async Task Request_RelayedCallerWithoutRequiresPerson_CapsExpiryAtDefaultExpiry()
    {
        var brain = new BrainId("b1");
        var context = new FakeNeuronContext(new NeuronId(brain, "runtime.confirmations", "confirmations"));
        var raiserNeuron = new NeuronId(brain, "flow.feature-pr", "run-1");
        var alice = new DigitalBrain.Access.Actor.Person("alice", "acc-1");
        context.Caller = new DigitalBrain.Access.Caller(alice, brain, via: raiserNeuron);

        var neuron = new ConfirmationsNeuron();
        NeuronAttachment.Attach(neuron, context);

        var plan = Plan("cf-relayed-cap") with
        {
            RequiresPerson = false,
            Expiry = TimeSpan.FromHours(48),
        };
        Assert.Null(await neuron.Request(plan));

        var pending = Assert.Single((await neuron.Read()).Pending);
        Assert.Equal(ConfirmationRules.DefaultExpiry, pending.Plan.Expiry);
    }

    [Fact]
    public async Task Request_StartsDurableExpiryReminder_AndStopsWhenCleared()
    {
        var brain = new BrainId("b1");
        var context = new FakeNeuronContext(new NeuronId(brain, "runtime.confirmations", "confirmations"));
        var neuron = new ConfirmationsNeuron();
        NeuronAttachment.Attach(neuron, context);
        context.Reminders.Target = neuron;

        var plan = Plan("cf-durable-exp") with { Expiry = TimeSpan.FromMinutes(1) };
        Assert.Null(await neuron.Request(plan));

        var reminders = await context.Reminders.Names();
        var expiryReminder = Assert.Single(reminders);

        context.Clock.Advance(TimeSpan.FromMinutes(2));
        await context.Reminders.Fire(expiryReminder, TestContext.Current.CancellationToken);

        var outcome = await neuron.Outcome("cf-durable-exp");
        Assert.NotNull(outcome);
        Assert.Equal(ConfirmationOutcome.Expired, outcome.Outcome);
        Assert.Empty(await context.Reminders.Names());
    }
}
