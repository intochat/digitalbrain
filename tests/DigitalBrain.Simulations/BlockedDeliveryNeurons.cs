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

internal sealed class OutageRelay : Neuron, IHandle<Ping>
{
    public Task HandleAsync(Ping synapse, CancellationToken cancellationToken)
        => SendAsync(NeuronId.For<RecoveringReceiver>(Id.Owner, "target"), new Ping());
}

internal sealed class RecoveringReceiver : Neuron, IHandle<Ping>
{
    private int _failuresLeft = 2;

    public Task HandleAsync(Ping synapse, CancellationToken cancellationToken)
    {
        if (_failuresLeft > 0)
        {
            var remaining = _failuresLeft--;
            throw new InvalidOperationException($"Receiver is still out; {remaining} failure(s) remain before it accepts.");
        }

        return Task.CompletedTask;
    }
}
