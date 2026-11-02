using System;
using System.Threading;
using System.Threading.Tasks;
using DigitalBrain;

namespace DigitalBrain.Runtime.Orleans.Log;

/// <summary>
/// Implements the reaction fault boundary: executes reaction handlers with up to 3 retries,
/// dead-letters poison signals, and advances the subscriber cursor past them.
/// </summary>
internal static class ReactionFaultBoundary
{
    public const int MaxRetries = 3;

    public static async Task<(TState State, bool Succeeded)> ExecuteReactionAsync<TState>(
        TState currentState,
        long position,
        string publisher,
        Signal signal,
        Func<TState, Signal, CancellationToken, Task<TState>> handler,
        Func<TState, CancellationToken, Task> commit,
        TimeProvider? time = null,
        int maxRetries = MaxRetries,
        Func<int, TimeSpan>? backoff = null,
        CancellationToken ct = default) where TState : NeuronState
    {
        ArgumentNullException.ThrowIfNull(currentState);
        ArgumentNullException.ThrowIfNull(signal);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(commit);

        time ??= TimeProvider.System;
        Exception? lastException = null;

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var nextState = await handler(currentState, signal, ct).ConfigureAwait(false);
                nextState = (TState)(nextState with { SignalCursor = position });
                await commit(nextState, ct).ConfigureAwait(false);
                return (nextState, true);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException && ex is not OperationCanceledException)
            {
                lastException = ex;
                if (attempt < maxRetries)
                {
                    var delay = backoff != null ? backoff(attempt) : TimeSpan.FromMilliseconds(10 * Math.Pow(2, attempt - 1));
                    if (delay > TimeSpan.Zero)
                    {
                        await Task.Delay(delay, time, ct).ConfigureAwait(false);
                    }
                }
            }
        }

        // Exhausted retries: create dead letter entry and advance cursor past poison signal
        var deadLetter = new DeadLetterEntry(
            position,
            publisher,
            signal.GetType().FullName ?? signal.GetType().Name,
            lastException?.Message ?? "Reaction failed after retries",
            time.GetUtcNow());

        var deadLetterState = (TState)(currentState with
        {
            DeadLetters = [.. currentState.DeadLetters, deadLetter],
            SignalCursor = position
        });

        await commit(deadLetterState, ct).ConfigureAwait(false);
        return (deadLetterState, false);
    }
}
