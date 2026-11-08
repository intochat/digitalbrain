namespace DigitalBrain.Spi;

/// <summary>
/// The brain's journal of published signals, read in sequence order. Entries below the floor have been trimmed;
/// a subscriber that fell below it receives a <see cref="Gap"/> instead of the missing signals.
/// </summary>
public interface ISignalHistory
{
    /// <summary>Signals <paramref name="publisher"/> published after <paramref name="since"/>, at most <paramref name="limit"/>, by sequence.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="limit"/> is outside 1..1000.</exception>
    Task<SignalPage> Read(NeuronId publisher, long since, int limit, CancellationToken ct);

    /// <summary>Signals of the whole brain after <paramref name="since"/>, at most <paramref name="limit"/>, by sequence.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="limit"/> is outside 1..1000.</exception>
    Task<SignalPage> Read(long since, int limit, CancellationToken ct);

    /// <summary>Signals of the whole brain carrying <paramref name="traceId"/> after <paramref name="since"/>, at most <paramref name="limit"/>, by sequence.</summary>
    /// <exception cref="ArgumentException"><paramref name="traceId"/> is null or empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="limit"/> is outside 1..1000.</exception>
    Task<SignalPage> ReadTrace(string traceId, long since, int limit, CancellationToken ct);

    /// <summary>
    /// Tails the whole brain's journal: yields the signals appended after <paramref name="since"/> (exclusive), in sequence order,
    /// then stays open and yields each new entry as it is appended, until <paramref name="ct"/> is cancelled. A sequence is never
    /// yielded twice. A runtime yields entries whose payload type is not loaded as-is (an untyped signal carrying the journaled alias),
    /// so a debugger sees every signal; trimmed entries below the floor are skipped. Batching for a wire (the HTTP tail) is the
    /// caller's concern.
    /// </summary>
    /// <remarks>The default implementation polls <see cref="Read(long, int, CancellationToken)"/>; a runtime overrides it.</remarks>
    /// <param name="since">The brain sequence to tail after; 0 for the whole held journal.</param>
    /// <param name="ct">Ends the tail.</param>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled (thrown by the enumerator).</exception>
    IAsyncEnumerable<JournaledSignal> Watch(long since, CancellationToken ct) => SignalTail.Poll(Read, since, SignalTail.PollInterval, ct);
}

/// <summary>The polling tail behind <see cref="ISignalHistory.Watch"/>.</summary>
internal static class SignalTail
{
    /// <summary>How long a polling tail waits after an empty read.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// Pages through <paramref name="read"/> after <paramref name="since"/>, waiting <paramref name="interval"/> (or until
    /// <paramref name="appended"/> completes, when given) after an empty page; the cursor only moves forward, so nothing is yielded twice.
    /// </summary>
    public static async IAsyncEnumerable<JournaledSignal> Poll(Func<long, int, CancellationToken, Task<SignalPage>> read, long since, TimeSpan interval,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct, Func<Task>? appended = null)
    {
        var cursor = Math.Max(0, since);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var wake = appended?.Invoke();
            var page = await read(cursor, SignalPage.MaxLimit, ct).ConfigureAwait(false);
            var moved = false;
            foreach (var item in page.Signals)
            {
                if (item.Sequence <= cursor) { continue; }
                cursor = item.Sequence;
                moved = true;
                yield return item;
            }

            if (moved) { continue; }
            if (wake is null) { await Task.Delay(interval, ct).ConfigureAwait(false); continue; }
            try { await wake.WaitAsync(interval, ct).ConfigureAwait(false); }
            catch (TimeoutException) { /* poll again */ }
        }
    }
}
