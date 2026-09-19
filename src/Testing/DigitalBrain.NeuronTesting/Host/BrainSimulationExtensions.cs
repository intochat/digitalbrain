using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Orleans;

namespace DigitalBrain.Testing;

public static class BrainSimulationExtensions
{
    public static async Task<SignalProbe<T>> Observe<T>(this IDigitalBrain brain, INeuron source, CancellationToken cancellationToken = default) where T : Signal
    {
        ArgumentNullException.ThrowIfNull(brain);
        var subscription = await brain.SubscribeAsync<T>(source, cancellationToken).ConfigureAwait(false);
        var simulated = brain as SimulatedBrain;
        var probe = new SignalProbe<T>(subscription, simulated?.BufferCapacity ?? new BrainOptions().BufferCapacity);
        simulated?.Track(probe);
        return probe;
    }

    public static BehaviorRun RunBehavior(this IDigitalBrain brain, Func<IDigitalBrain, CancellationToken, Task> body, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(brain);
        var run = new BehaviorRun(brain, body, cancellationToken);
        if (brain is SimulatedBrain simulated) { simulated.Track(run); }
        return run;
    }

    public static IGrainFactory Grains(this IDigitalBrain brain) => Requires(brain).Grains;

    public static IDigitalBrain Client(this IDigitalBrain brain) => Requires(brain).Client;

    public static Task DeactivateAsync(this IDigitalBrain brain, INeuron neuron, CancellationToken cancellationToken = default)
        => Requires(brain).DeactivateAsync(neuron, cancellationToken);

    public static Task RestartSiloAsync(this IDigitalBrain brain, CancellationToken cancellationToken = default)
        => Requires(brain).RestartSiloAsync(cancellationToken);

    private static SimulatedBrain Requires(IDigitalBrain brain)
        => brain as SimulatedBrain ?? throw new InvalidOperationException("This operation requires a brain started by DigitalBrainSimulation.");
}
