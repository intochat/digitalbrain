using DigitalBrain.Abstractions;
using Xunit;

namespace DigitalBrain.TestingTests;

public sealed class CapabilityReificationContracts(TestingFixture fixture)
{
    [Fact(DisplayName =
        "Neuron-to-neuron typed call reifies CapabilityRequested on both journals and CapabilityCompleted on the caller")]
    public async Task NeuronToNeuronCallReifiesRequestedAndCompletedFacts()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var caller = test.Neuron<ICapabilityCaller>(TestingScenario.CapabilityCaller);
        var target = test.Neuron<ICapabilityTarget>(TestingScenario.CapabilityTarget);

        await test.Client.SendAsync<ICapabilityCaller>(caller.Id.Name, new CapabilityPing(), cancellationToken);

        var stimulus = await caller.Incoming.NextAsync<CapabilityPing>(cancellationToken);
        var requested = await caller.Outgoing.NextAsync<CapabilityRequested>(cancellationToken);
        var completed = await caller.Outgoing.NextAsync<CapabilityCompleted>(cancellationToken);
        var received = await target.Incoming.NextAsync<CapabilityRequested>(cancellationToken);

        Assert.Equal(typeof(ICapabilityTarget).FullName, requested.Synapse.Contract);
        Assert.Equal(nameof(ICapabilityTarget.Poke), requested.Synapse.Method);
        Assert.Equal(target.Id, requested.Synapse.Target);
        Assert.Equal(caller.Id, requested.Caller);
        Assert.Equal(stimulus.SynapseId, requested.CausationId);
        Assert.Equal(stimulus.CorrelationId, requested.CorrelationId);

        Assert.Equal(requested.SynapseId, received.SynapseId);
        Assert.Equal(requested.CorrelationId, received.CorrelationId);
        Assert.Equal(requested.Caller, received.Caller);
        Assert.Equal(requested.Synapse.Contract, received.Synapse.Contract);
        Assert.Equal(requested.Synapse.Method, received.Synapse.Method);
        Assert.Equal(requested.Synapse.Target, received.Synapse.Target);

        Assert.Equal(requested.SynapseId, completed.Synapse.Request);
        Assert.Equal(requested.SynapseId, completed.CausationId);
        Assert.Equal(requested.CorrelationId, completed.CorrelationId);
        Assert.Equal(caller.Id, completed.Caller);
        Assert.Equal(JournalKind.Outgoing, completed.Direction);
        Assert.Empty(await caller.Outgoing.ReadAsync<CapabilityFailed>(cancellationToken: cancellationToken));
        Assert.Empty(await caller.Outgoing.ReadAsync<CapabilityRejected>(cancellationToken: cancellationToken));
    }

    // A capability request commits its cause mid-turn. Carrying that commit into the turn's
    // checkpoint left a retracted turn with its delivery still marked handled, so the outbox's own
    // redelivery was swallowed by the duplicate guard and the handler never ran to completion —
    // deafness with an unbroken journal and no failure recorded anywhere.
    [Fact(DisplayName =
        "a turn retracted after its capability request is redelivered and its cause is journaled once",
        Timeout = 60_000)]
    public async Task TurnRetractedAfterACapabilityRequestIsRedelivered()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var caller = test.Neuron<ICapabilityCaller>(TestingScenario.CapabilityCaller);

        await test.Client.SendAsync<ICapabilityCaller>(
            caller.Id.Name,
            new CapabilityPingRetractedOnce(),
            cancellationToken);

        var settled = await caller.Outgoing.NextAsync<CapabilityRequested>(cancellationToken);
        while (!string.Equals(settled.Synapse.Method, nameof(ICapabilityTarget.Settle), StringComparison.Ordinal))
        {
            settled = await caller.Outgoing.NextAsync<CapabilityRequested>(cancellationToken);
        }

        var causes = await caller.Incoming.ReadAsync<CapabilityPingRetractedOnce>(
            cancellationToken: cancellationToken);
        var requests = await caller.Outgoing.ReadAsync<CapabilityRequested>(
            cancellationToken: cancellationToken);

        Assert.Single(causes);
        Assert.Equal(causes[0].CorrelationId, settled.CorrelationId);
        Assert.Equal(
            2,
            requests.Count(request => string.Equals(
                request.Synapse.Method,
                nameof(ICapabilityTarget.Poke),
                StringComparison.Ordinal)));
    }

    // The other half of the same contract, and the one the MCP authorization rail parks on: a
    // failure marked settled is the delivery's answer, so the fact stays received and the outbox
    // stops rather than replaying a turn whose outcome is already decided.
    [Fact(DisplayName =
        "a settled failure consumes its delivery, so the turn runs once and its cause stays journaled",
        Timeout = 60_000)]
    public async Task SettledFailureConsumesItsDelivery()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var caller = test.Neuron<ICapabilityCaller>(TestingScenario.CapabilityCaller);

        await test.Client.SendAsync<ICapabilityCaller>(
            caller.Id.Name,
            new CapabilityPingSettled(),
            cancellationToken);
        await test.Client.SendAsync<ICapabilityCaller>(
            caller.Id.Name,
            new CapabilityPing(),
            cancellationToken);

        // The ordinary ping is queued behind the settled one, so hearing it proves the settled
        // delivery has left the outbox rather than that it has not been retried yet.
        _ = await caller.Incoming.NextAsync<CapabilityPing>(cancellationToken);

        var settledCauses = await caller.Incoming.ReadAsync<CapabilityPingSettled>(
            cancellationToken: cancellationToken);
        var requests = await caller.Outgoing.ReadAsync<CapabilityRequested>(
            cancellationToken: cancellationToken);

        Assert.Single(settledCauses);
        Assert.Equal(2, requests.Count);
    }
}
