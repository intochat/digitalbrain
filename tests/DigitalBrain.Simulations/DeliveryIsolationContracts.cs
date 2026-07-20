using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Simulations;

public sealed class DeliveryIsolationContracts
{
    private static readonly Dictionary<string, string> NoValues = new(StringComparer.Ordinal);

    [Fact(DisplayName = "a sender mutation cannot change a delivery after the kernel stamps it")]
    public async Task SenderMutationCannotChangeAStampedDelivery()
    {
        await SimulationCluster.StartAsync();

        var simulation = new Simulation();
        simulation.OpenBrain("delivery-isolation");

        await simulation.SendAsync("Ping", nameof(MutatingEmitter), "source", NoValues);
        await simulation.AwaitHandledAsync(nameof(MutableRecorder), "target", nameof(MutableFact));

        var sent = Assert.Single((await simulation.ReadJournalAsync(
            JournalKind.Outgoing,
            nameof(MutatingEmitter),
            "source",
            afterSequence: 0)).Delta);
        var received = Assert.Single((await simulation.ReadJournalAsync(
            JournalKind.Incoming,
            nameof(MutableRecorder),
            "target",
            afterSequence: 0)).Delta);

        Assert.Equal(sent.SynapseId, received.SynapseId);
        Assert.Equal("before", Assert.IsType<MutableFact>(sent.Synapse).Text);
        Assert.Equal("before", Assert.IsType<MutableFact>(received.Synapse).Text);
    }
}

[GenerateSerializer]
[Alias("db.test.mutable-fact")]
internal sealed record MutableFact : Synapse
{
    [Id(0)]
    public string Text { get; set; } = "";
}

internal sealed class MutatingEmitter : Neuron, IHandle<Ping>, IEmit<MutableFact>
{
    public async Task HandleAsync(Ping synapse, CancellationToken cancellationToken)
    {
        var emitted = new MutableFact { Text = "before" };

        await SendAsync(NeuronId.For<MutableRecorder>(Id.Owner, "target"), emitted);

        emitted.Text = "after";
    }
}

internal sealed class MutableRecorder : Neuron, IHandle<MutableFact>
{
    public Task HandleAsync(MutableFact synapse, CancellationToken cancellationToken)
    {
        synapse.Text = "changed by receiver";

        return Task.CompletedTask;
    }
}
