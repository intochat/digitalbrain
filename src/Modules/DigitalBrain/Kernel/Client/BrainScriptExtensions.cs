using System.Runtime.CompilerServices;
using DigitalBrain.Contracts;

namespace DigitalBrain.Core;

public static class BrainScriptExtensions
{
    public static async IAsyncEnumerable<T> On<T>(this IDigitalBrain brain, INeuron source,
        [EnumeratorCancellation] CancellationToken cancellationToken = default) where T : Signal
    {
        ArgumentNullException.ThrowIfNull(brain);
        await using var subscription = await brain.SubscribeAsync<T>(source, cancellationToken).ConfigureAwait(false);
        await foreach (var signal in subscription.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        { yield return signal; }
    }
}
