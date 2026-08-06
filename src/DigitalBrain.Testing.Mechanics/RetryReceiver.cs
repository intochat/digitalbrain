using System.Collections.Concurrent;

namespace DigitalBrain.Testing.Mechanics;

public sealed class RetryReceiver : Neuron, INeuron<RetryPulse>
{
    private static readonly ConcurrentDictionary<string, RetryGate> Gates = new(StringComparer.Ordinal);

    public static void Reset(string key) => Gates[key] = new RetryGate();

    public static Task WaitForFirstAttemptAsync(string key, CancellationToken cancellationToken)
        => GateFor(key).FirstAttempt.Task.WaitAsync(cancellationToken);

    public static int AttemptsFor(string key) => Volatile.Read(ref GateFor(key).Attempts);

    public static void AllowDelivery(string key) => Volatile.Write(ref GateFor(key).Allowed, 1);

    public Task HandleAsync(RetryPulse synapse, CancellationToken cancellationToken)
    {
        var gate = GateFor(synapse.Key);
        Interlocked.Increment(ref gate.Attempts);
        gate.FirstAttempt.TrySetResult();
        if (Volatile.Read(ref gate.Allowed) == 0)
        {
            throw new InvalidOperationException("The mechanical retry gate is closed.");
        }

        return Task.CompletedTask;
    }

    private static RetryGate GateFor(string key) => Gates.GetOrAdd(key, static _ => new RetryGate());

    private sealed class RetryGate
    {
        internal TaskCompletionSource FirstAttempt { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal int Attempts;

        internal int Allowed;
    }
}
