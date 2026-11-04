using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DigitalBrain;
using DigitalBrain.Runtime.Orleans.Signalling;
using DigitalBrain.Runtime.Orleans.Synapses;
using Microsoft.Extensions.Options;
using Orleans;

namespace DigitalBrain.Runtime.Orleans.Log;

/// <summary>
/// The append-only, durable segmented signal log ({brain}/log/{segment}) acting as the single source of truth
/// for events within a brain. Emits monotonic unified sequence positions and fire-and-forget notifications.
/// </summary>
internal sealed class BrainSignalLog(
    string brainId,
    BrainLogs logs,
    IGrainFactory? grains = null,
    TimeProvider? time = null,
    IOptions<RetentionOptions>? retentionOptions = null) : IBrainSignalLog
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly TimeSpan _staleCursorAfter = retentionOptions?.Value.StaleCursorAfter ?? TimeSpan.FromDays(7);

    public string BrainId => brainId;

    public async Task<BrainLog> GetUnderlyingLogAsync(CancellationToken ct = default)
        => await logs.For(brainId).WaitAsync(ct).ConfigureAwait(false);

    /// <summary>Returns the current highest monotonic position read in the unified brain signal log.</summary>
    public async Task<long> GetLatestPositionAsync(CancellationToken ct = default)
    {
        var log = await GetUnderlyingLogAsync(ct).ConfigureAwait(false);
        return log.LastPosition;
    }

    /// <summary>Appends a single signal to the brain log, returning the unified monotonic sequence N.</summary>
    public async Task<long> AppendSignalAsync(NeuronId publisher, Signal signal, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(signal);
        return await AppendSignalsAsync(publisher, [signal], ct).ConfigureAwait(false);
    }

    /// <summary>Appends a batch of signals to the brain log, returning the unified monotonic sequence N.</summary>
    public async Task<long> AppendSignalsAsync(NeuronId publisher, IReadOnlyList<Signal> signals, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(signals);
        if (signals.Count == 0)
        {
            return await GetLatestPositionAsync(ct).ConfigureAwait(false);
        }

        var log = await GetUnderlyingLogAsync(ct).ConfigureAwait(false);
        var (_, _, lastPosition) = await log.AppendEntryWithPosition(publisher.ToString(), "state", null, BrainRecordChange.None, [], publisher.ToString(), signals, null, ct).ConfigureAwait(false);
        return lastPosition;
    }

    /// <summary>Emits fire-and-forget [OneWay] position notification to registered subscribers in constant time.</summary>
    public Task NotifySubscribersAsync(IEnumerable<string> subscribers, long position)
    {
        if (grains is null || subscribers is null)
        {
            return Task.CompletedTask;
        }

        foreach (var subscriber in subscribers)
        {
            if (string.IsNullOrWhiteSpace(subscriber))
            {
                continue;
            }

            try
            {
                var target = grains.GetGrain<ISignalSubscriber>(subscriber);
                _ = target.Notify(position);
            }
            catch (Exception)
            {
                // Fire-and-forget notification delivery errors are ignored; on-demand catch-up heals lagging grains.
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Lazily catches up a subscriber from its current SignalCursor up to targetPosition,
    /// executing reactions within the fault boundary.
    /// </summary>
    public async Task<TState> EnsureCaughtUpAsync<TState>(
        NeuronId subscriberId,
        TState currentState,
        Func<TState, Signal, CancellationToken, Task<TState>> handler,
        Func<TState, CancellationToken, Task> commit,
        long targetPosition,
        CancellationToken ct = default) where TState : NeuronState
    {
        ArgumentNullException.ThrowIfNull(currentState);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(commit);

        if (targetPosition <= currentState.SignalCursor)
        {
            return currentState;
        }

        var log = await GetUnderlyingLogAsync(ct).ConfigureAwait(false);
        var state = currentState;

        while (state.SignalCursor < targetPosition)
        {
            ct.ThrowIfCancellationRequested();
            var readLimit = (int)Math.Min(SignalPage.MaxLimit, targetPosition - state.SignalCursor);
            var slice = await log.BrainSignals(state.SignalCursor, readLimit, ct).ConfigureAwait(false);

            if (slice.Held.Length == 0)
            {
                // No more signals available in log up to targetPosition; advance cursor to target
                state = (TState)(state with { SignalCursor = targetPosition });
                await commit(state, ct).ConfigureAwait(false);
                break;
            }

            foreach (var stored in slice.Held)
            {
                ct.ThrowIfCancellationRequested();
                Signal signal;
                try
                {
                    signal = log.Payloads.Types.TryResolve(stored.Payload.Type, out _)
                        ? log.Payloads.DecodeSignal(stored.Payload)
                        : new UnknownSignal(stored.Payload.Type);
                }
                catch (Exception)
                {
                    signal = new UnknownSignal(stored.Payload.Type);
                }

                var (nextState, _) = await ReactionFaultBoundary.ExecuteReactionAsync(
                    state,
                    stored.Position,
                    stored.Publisher,
                    signal,
                    handler,
                    commit,
                    _time,
                    ct: ct).ConfigureAwait(false);

                state = nextState;
            }
        }

        return state;
    }

    /// <summary>Checks whether a subscriber is considered stale/quarantined based on inactivity.</summary>
    public bool IsQuarantined(DateTimeOffset lastActiveAt, DateTimeOffset? now = null)
    {
        var current = now ?? _time.GetUtcNow();
        return lastActiveAt < (current - _staleCursorAfter);
    }

    /// <summary>
    /// Computes the safe log compaction floor by excluding quarantined subscribers (> 7 days stale)
    /// to prevent compaction deadlocks.
    /// </summary>
    public long ComputeCompactionFloor(
        long logLatestPosition,
        IEnumerable<(string SubscriberId, long Cursor, DateTimeOffset LastActiveAt)> subscribers,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(subscribers);
        var current = now ?? _time.GetUtcNow();

        var activeCursors = subscribers
            .Where(s => !IsQuarantined(s.LastActiveAt, current))
            .Select(s => s.Cursor)
            .ToList();

        if (activeCursors.Count == 0)
        {
            return logLatestPosition;
        }

        return activeCursors.Min();
    }
}
