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

        await test.Client.SendAsync<ICapabilityCaller>(caller.Id.Name, new CapabilityPing());

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
}
