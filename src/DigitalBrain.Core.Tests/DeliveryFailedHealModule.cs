namespace DigitalBrain.Core.Tests;

public sealed record HealedPath(SynapseRef FailedFact, NeuronId FailedReceiver, string Reason) : Synapse;

// Self-heal spine: DeliveryFailed is vocabulary other neurons compose on — not try/catch
// inside the original speaker. Hears Core's terminal and Emits an alternate path fact.
public sealed class FailureHealer : Neuron, INeuron<DeliveryFailed>
{
    public Task HandleAsync(DeliveryFailed fact, CancellationToken cancellationToken)
    {
        Emit(new HealedPath(fact.Fact, fact.Receiver, fact.Reason));
        return Task.CompletedTask;
    }
}

public sealed class FailureHealObserver : Neuron, INeuron<HealedPath>
{
    public Task HandleAsync(HealedPath fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
