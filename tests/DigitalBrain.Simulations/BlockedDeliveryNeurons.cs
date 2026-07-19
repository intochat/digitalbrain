using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;

namespace DigitalBrain.Simulations;

[GenerateSerializer]
[Alias("db.test.blocked")]
internal sealed record Blocked : Synapse;

internal sealed class UnreachableReceiver : Neuron, IHandle<Blocked>
{
    public Task HandleAsync(Blocked synapse, CancellationToken cancellationToken)
        => throw new InvalidOperationException("This receiver never accepts a delivery, so the sender keeps it pending in the outbox.");
}

internal sealed class Splitter : Neuron, IHandle<Ping>, IEmit<Blocked>, IEmit<Ping>
{
    public async Task HandleAsync(Ping synapse, CancellationToken cancellationToken)
    {
        await SendAsync(NeuronId.For<UnreachableReceiver>(Id.Owner, "unreachable"), new Blocked());
        await SendAsync(NeuronId.For<Echo>(Id.Owner, "reachable"), new Ping());
    }
}
