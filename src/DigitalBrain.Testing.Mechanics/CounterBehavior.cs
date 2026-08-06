namespace DigitalBrain.Testing.Mechanics;

public sealed class CounterBehavior : Neuron<CounterState>, INeuron<CounterInput>
{
    private static int nextInstance;
    private readonly int instance = Interlocked.Increment(ref nextInstance);

    public Task HandleAsync(CounterInput synapse, CancellationToken cancellationToken)
    {
        State.Value++;
        Emit(new CounterReported(State.Value, instance));
        return Task.CompletedTask;
    }
}
