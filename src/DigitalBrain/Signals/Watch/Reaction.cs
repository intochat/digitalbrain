namespace DigitalBrain.Signals.Watch;

// React<T>: rebuild once when registered, then once per signal. The set stream is opened with
// coalesce, so a burst queued behind one signal is one rebuild: the handler reads authoritative
// state, and one run after a burst is both correct and cheaper.
internal static class Reaction
{
    public static async Task Run<T>(ISignalWatch brain, Sources sources, Func<CancellationToken, Task> rebuild, CancellationToken cancellationToken)
        where T : DigitalBrain.Signal
    {
        ArgumentNullException.ThrowIfNull(rebuild);
        try
        {
            await foreach (var _ in SourceSet.Open<T>(brain, sources, () => rebuild(cancellationToken), coalesce: true, cancellationToken)
                .WithCancellation(cancellationToken).ConfigureAwait(false))
            { await rebuild(cancellationToken).ConfigureAwait(false); }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }
}
