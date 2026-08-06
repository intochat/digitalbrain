namespace DigitalBrain.Testing.Mechanics;

public sealed class PassiveProbeBehavior : Neuron, INeuron<PassiveProbeSynapse>
{
    private static int constructions;

    public PassiveProbeBehavior() => Interlocked.Increment(ref constructions);

    public static int Constructions => Volatile.Read(ref constructions);

    public static void Reset() => Volatile.Write(ref constructions, 0);

    public Task HandleAsync(PassiveProbeSynapse synapse, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
