namespace DigitalBrain.Core.Tests;

public sealed record FragileWork(string Note) : Synapse;

public sealed record FragileSideEffect(string Note) : Synapse;

// Shared gate for the throw/redeliver proof: the silo and the test host share one
// InProcess cluster, so the singleton instance Compose registers is the live switch.
public sealed class FragilityGate
{
    public bool Refuse { get; set; }
}

// Stages a side-effect Emit then optionally throws — ClearTurn must discard both the
// inbound heard line and the staged said before any durable commit.
public sealed class FragileReceiver(FragilityGate gate) : Neuron, INeuron<FragileWork>
{
    public Task HandleAsync(FragileWork fact, CancellationToken cancellationToken)
    {
        Emit(new FragileSideEffect(fact.Note));
        if (gate.Refuse)
        {
            throw new InvalidOperationException("handler refused the fragile turn");
        }

        return Task.CompletedTask;
    }
}

public sealed class SideEffectObserver : Neuron, INeuron<FragileSideEffect>
{
    public Task HandleAsync(FragileSideEffect fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
