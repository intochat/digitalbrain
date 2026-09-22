using System.Runtime.CompilerServices;
using DigitalBrain.Contracts;

namespace DigitalBrain.Core;

public static class BrainListen
{
    public static async IAsyncEnumerable<T> On<T>(this IDigitalBrain brain, INeuron source, [EnumeratorCancellation] CancellationToken cancellation = default)
        where T : Signal
    {
        ArgumentNullException.ThrowIfNull(brain);
        ArgumentNullException.ThrowIfNull(source);
        await using var subscription = await brain.SubscribeAsync<T>(source, cancellation).ConfigureAwait(false);
        await foreach (var signal in subscription.ReadAllAsync(cancellation).ConfigureAwait(false))
        {
            yield return signal;
        }
    }
}