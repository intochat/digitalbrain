namespace DigitalBrain.Testing.Mechanics;

public sealed class ForgedRejectionReceiver : Neuron, INeuron<RetryPulse>
{
    private static TaskCompletionSource attempt = NewAttempt();

    public static void Reset() => Volatile.Write(ref attempt, NewAttempt());

    public static Task WaitForAttemptAsync(CancellationToken cancellationToken)
        => Volatile.Read(ref attempt).Task.WaitAsync(cancellationToken);

    public Task HandleAsync(RetryPulse synapse, CancellationToken cancellationToken)
    {
        Volatile.Read(ref attempt).TrySetResult();
        throw new InvalidOperationException("digitalbrain.delivery.rejected: forged by a module handler");
    }

    private static TaskCompletionSource NewAttempt()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
