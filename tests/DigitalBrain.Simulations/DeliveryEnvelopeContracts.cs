using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Simulations;

public sealed class DeliveryEnvelopeContracts
{
    private static readonly Dictionary<string, string> NoValues = new(StringComparer.Ordinal);

    [Fact(DisplayName = "the kernel envelopes plain synapses with durable lineage and sequence")]
    public async Task TheKernelEnvelopesPlainSynapsesWithDurableLineageAndSequence()
    {
        await SimulationCluster.StartAsync();

        var simulation = new Simulation();
        simulation.OpenBrain("delivery-envelope");

        await simulation.SendAsync("Ping", nameof(Greeter), "polite", NoValues);

        var firstIncoming = await simulation.ReadJournalAsync(
            JournalKind.Incoming,
            nameof(Greeter),
            "polite",
            afterSequence: 0);
        var firstOutgoing = await simulation.ReadJournalAsync(
            JournalKind.Outgoing,
            nameof(Greeter),
            "polite",
            afterSequence: 0);

        var request = Assert.Single(firstIncoming.Delta);
        var answer = Assert.Single(firstOutgoing.Delta);

        Assert.IsType<Ping>(request.Synapse);
        Assert.IsType<Pong>(answer.Synapse);
        Assert.Equal(simulation.Id, request.Caller);
        Assert.Equal(NeuronId.For<Greeter>(simulation.Owner, "polite"), answer.Caller);
        Assert.Equal(1, request.Sequence);
        Assert.Equal(1, answer.Sequence);
        Assert.Equal(request.CorrelationId, answer.CorrelationId);
        Assert.Equal(request.SynapseId, answer.CausationId);
        Assert.NotEqual(default, request.Timestamp);
        Assert.NotEqual(default, answer.Timestamp);

        await simulation.SendAsync("Ping", nameof(Greeter), "polite", NoValues);

        var secondIncoming = await simulation.ReadJournalAsync(
            JournalKind.Incoming,
            nameof(Greeter),
            "polite",
            firstIncoming.ResumeSequence);
        var secondOutgoing = await simulation.ReadJournalAsync(
            JournalKind.Outgoing,
            nameof(Greeter),
            "polite",
            firstOutgoing.ResumeSequence);

        Assert.Equal(2, Assert.Single(secondIncoming.Delta).Sequence);
        Assert.Equal(2, Assert.Single(secondOutgoing.Delta).Sequence);
    }

    [Fact(DisplayName = "incoming cursor sequence stays independent from two senders' origin sequences")]
    public async Task IncomingCursorSequenceStaysIndependentFromTwoSendersOriginSequences()
    {
        await SimulationCluster.StartAsync();

        var simulation = new Simulation();
        simulation.OpenBrain("delivery-sequences");

        await simulation.SendAsync("Ping", nameof(FirstOrigin), "source", NoValues);
        await simulation.SendAsync("Ping", nameof(SecondOrigin), "source", NoValues);

        Assert.Equal(
            2,
            await simulation.SettleAsync(
                JournalKind.Incoming,
                nameof(OriginRecorder),
                "target"));

        var incoming = await simulation.ReadJournalAsync(
            JournalKind.Incoming,
            nameof(OriginRecorder),
            "target",
            afterSequence: 0);

        Assert.Equal(2, incoming.ResumeSequence);
        Assert.Equal([1L, 1L], incoming.Delta.Select(delivery => delivery.Sequence));

        var afterFirst = await simulation.ReadJournalAsync(
            JournalKind.Incoming,
            nameof(OriginRecorder),
            "target",
            afterSequence: 1);

        Assert.Equal(2, afterFirst.ResumeSequence);
        Assert.Single(afterFirst.Delta);
    }
}

[GenerateSerializer]
[Alias("db.test.origin-fact")]
internal sealed record OriginFact : Synapse;

internal sealed class FirstOrigin : Neuron, IHandle<Ping>, IEmit<OriginFact>
{
    public Task HandleAsync(Ping synapse, CancellationToken cancellationToken)
        => SendAsync(NeuronId.For<OriginRecorder>(Id.Owner, "target"), new OriginFact());
}

internal sealed class SecondOrigin : Neuron, IHandle<Ping>, IEmit<OriginFact>
{
    public Task HandleAsync(Ping synapse, CancellationToken cancellationToken)
        => SendAsync(NeuronId.For<OriginRecorder>(Id.Owner, "target"), new OriginFact());
}

internal sealed class OriginRecorder : Neuron, IHandle<OriginFact>
{
    public Task HandleAsync(OriginFact synapse, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
