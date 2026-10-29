namespace DigitalBrain.Runtime.Orleans;

/// <summary>How a silo proves it is the single writer of a brain's log (contract moved from os Authority; BrainLog follows in 04-05).</summary>
public interface IBrainLogOwnership
{
    /// <summary>Claims the brain before the log touches its key or files.</summary>
    /// <exception cref="BrainNotOwnedException">Another writer holds the brain.</exception>
    Task<IBrainLogLease> ClaimAsync(string directory, string brainId, CancellationToken ct);
}

/// <summary>A held claim. Once it can no longer be trusted the log refuses to write.</summary>
public interface IBrainLogLease : IAsyncDisposable
{
    /// <summary>False once the claim is lost; never throws.</summary>
    bool IsHeld { get; }

    /// <summary>Returns a mark for <see cref="EndWrite"/>.</summary>
    /// <exception cref="BrainNotOwnedException">The claim is lost.</exception>
    long BeginWrite();

    /// <summary>Ends a write started at <paramref name="started"/>.</summary>
    /// <exception cref="BrainNotOwnedException">The claim was lost, or the write took longer than the write cap.</exception>
    void EndWrite(long started);
}

/// <summary>Another writer owns the brain: this silo must not open or create its log.</summary>
public sealed class BrainNotOwnedException : IOException
{
    /// <summary>Creates the exception for <paramref name="brainId"/>.</summary>
    public BrainNotOwnedException(string brainId, Exception? inner = null)
        : base($"Brain '{brainId}' is owned by another writer.", inner) => BrainId = brainId;

    /// <summary>The brain.</summary>
    public string BrainId { get; }
}

/// <summary>Running totals of brain-log ownership events in this silo.</summary>
public interface IBrainOwnershipCounters
{
    /// <summary>The current totals.</summary>
    BrainOwnershipCounts Snapshot();
}

/// <summary>Ownership event totals.</summary>
/// <param name="Mechanism">The ownership mechanism, such as <c>blob-lease</c>.</param>
/// <param name="Acquired">Claims taken.</param>
/// <param name="Refused">Claims refused.</param>
/// <param name="Lost">Leases lost because a renewal was refused.</param>
/// <param name="Released">Leases released.</param>
/// <param name="Renewed">Renewals accepted.</param>
/// <param name="Fenced">Leases lost to the fence or the write cap.</param>
public sealed record BrainOwnershipCounts(string Mechanism, long Acquired, long Refused, long Lost, long Released, long Renewed, long Fenced);
