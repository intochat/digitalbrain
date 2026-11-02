using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DigitalBrain;

/// <summary>
/// Append-only, durable segmented signal log interface ({brain}/log/{segment}) acting as the single source of truth
/// for events within a brain. Emits monotonic unified sequence positions and fire-and-forget notifications.
/// </summary>
public interface IBrainSignalLog
{
    /// <summary>The identifier of the brain owning this signal log.</summary>
    string BrainId { get; }

    /// <summary>Returns the current highest monotonic position read in the unified brain signal log.</summary>
    Task<long> GetLatestPositionAsync(CancellationToken ct = default);

    /// <summary>Appends a single signal to the brain log, returning the unified monotonic sequence N.</summary>
    Task<long> AppendSignalAsync(NeuronId publisher, Signal signal, CancellationToken ct = default);

    /// <summary>Appends a batch of signals to the brain log, returning the unified monotonic sequence N.</summary>
    Task<long> AppendSignalsAsync(NeuronId publisher, IReadOnlyList<Signal> signals, CancellationToken ct = default);

    /// <summary>Emits fire-and-forget [OneWay] position notification to registered subscribers in constant time.</summary>
    Task NotifySubscribersAsync(IEnumerable<string> subscribers, long position);

    /// <summary>
    /// Lazily catches up a subscriber from its current SignalCursor up to targetPosition,
    /// executing reactions within the fault boundary.
    /// </summary>
    Task<TState> EnsureCaughtUpAsync<TState>(
        NeuronId subscriberId,
        TState currentState,
        Func<TState, Signal, CancellationToken, Task<TState>> handler,
        Func<TState, CancellationToken, Task> commit,
        long targetPosition,
        CancellationToken ct = default) where TState : NeuronState;

    /// <summary>Checks whether a subscriber is considered stale/quarantined based on inactivity.</summary>
    bool IsQuarantined(DateTimeOffset lastActiveAt, DateTimeOffset? now = null);

    /// <summary>
    /// Computes the safe log compaction floor by excluding quarantined subscribers (> 7 days stale)
    /// to prevent compaction deadlocks.
    /// </summary>
    long ComputeCompactionFloor(
        long logLatestPosition,
        IEnumerable<(string SubscriberId, long Cursor, DateTimeOffset LastActiveAt)> subscribers,
        DateTimeOffset? now = null);
}
