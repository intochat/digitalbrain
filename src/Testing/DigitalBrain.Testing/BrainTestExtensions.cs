using DigitalBrain.Contracts;
using DigitalBrain.Core;

namespace DigitalBrain.Testing;

public static class BrainTestExtensions
{
    public static async Task<SignalProbe<T>> Observe<T>(this IDigitalBrain brain, INeuron source, CancellationToken cancellationToken = default) where T : Signal
    {
        ArgumentNullException.ThrowIfNull(brain);
        var subscription = await brain.SubscribeAsync<T>(source, cancellationToken).ConfigureAwait(false);
        var tracked = brain as ITrackedBrain;
        var probe = new SignalProbe<T>(subscription, tracked?.BufferCapacity ?? new BrainOptions().BufferCapacity, tracked?.Execution ?? new());
        tracked?.Track(probe);
        return probe;
    }

    public static BehaviorRun RunBehavior(this IDigitalBrain brain, Func<IDigitalBrain, CancellationToken, Task> body, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(brain);
        var run = new BehaviorRun(brain, body, cancellationToken);
        (brain as ITrackedBrain)?.Track(run);
        return run;
    }
}