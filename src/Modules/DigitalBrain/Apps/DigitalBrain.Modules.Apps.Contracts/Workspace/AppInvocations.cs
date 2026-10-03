using System.Runtime.CompilerServices;
using DigitalBrain.Apps.Signals;

namespace DigitalBrain.Apps;

public static class AppInvocations
{
    // Subscribe before recovery so a new invocation cannot fall between the two paths.
    // Delivery across process restarts remains at least once; handlers must be idempotent.
    public static async IAsyncEnumerable<AppInvoked> Invocations(this IApp app, IDigitalBrain brain,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var subscription = await brain.SubscribeAsync<AppInvoked>(app, cancellationToken);
        var pending = await app.Pending();
        var recovered = pending.Select(x => x.Id).ToHashSet();
        foreach (var invocation in pending)
        { yield return new(invocation.Id, invocation.Operation, invocation.Input); }
        await foreach (var invoked in subscription.ReadAllAsync(cancellationToken))
        {
            if (recovered.Remove(invoked.InvocationId)) { continue; }
            // Completed history may already be pruned. Pending is also safe over the script
            // HTTP edge, which does not preserve the server's exception types.
            if ((await app.Pending()).Any(current => current.Id == invoked.InvocationId))
            { yield return invoked; }
        }
    }
}
