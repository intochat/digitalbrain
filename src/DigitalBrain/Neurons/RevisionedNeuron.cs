namespace DigitalBrain;

/// <summary>A state that counts its revisions and remembers the operations it applied.</summary>
/// <typeparam name="TState">The state record itself.</typeparam>
public interface IRevisioned<TState> where TState : IRevisioned<TState>
{
    /// <summary>The revision; every applied operation advances it by one.</summary>
    long Revision { get; }

    /// <summary>The ids of the applied operations.</summary>
    IReadOnlyCollection<string> Operations { get; }

    /// <summary>The state one revision later, with <paramref name="operationId"/> recorded.</summary>
    TState Advanced(string operationId);
}

/// <summary>A commit expected another revision than the current one.</summary>
public sealed class RevisionConflictException : Exception
{
    /// <summary>A conflict with no detail.</summary>
    public RevisionConflictException() { }

    /// <summary>A conflict with a message.</summary>
    public RevisionConflictException(string message) : base(message) { }

    /// <summary>A conflict with a message and a cause.</summary>
    public RevisionConflictException(string message, Exception innerException) : base(message, innerException) { }

    /// <summary>A conflict reporting the current revision.</summary>
    public RevisionConflictException(string message, long currentRevision) : base(message) => CurrentRevision = currentRevision;

    /// <summary>The revision the state is at.</summary>
    public long CurrentRevision { get; }

    /// <summary>The refusal header the runtime edge answers with.</summary>
    public const string Code = "RevisionConflict";
}

/// <summary>A neuron whose state carries a revision checked optimistically on each commit, and whose operations are idempotent.</summary>
public abstract class RevisionedNeuron<TState> : Neuron<TState> where TState : class, IRevisioned<TState>, new()
{
    /// <summary>The current revision.</summary>
    protected long Revision => State.Revision;

    /// <summary>
    /// Applies <paramref name="change"/> when <paramref name="expectedRevision"/> is current and saves it with the signal <paramref name="changed"/> builds.
    /// False when the operation was already applied: a replay changes nothing and publishes nothing.
    /// </summary>
    /// <exception cref="RevisionConflictException">The current revision is not <paramref name="expectedRevision"/>.</exception>
    protected async Task<bool> Commit(string operationId, long expectedRevision, Func<TState, TState> change, Func<TState, Signal> changed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(changed);
        if (State.Operations.Contains(operationId, StringComparer.Ordinal)) { return false; }
        RequireRevision(expectedRevision, State.Revision);
        var next = change(State).Advanced(operationId);
        await Save(next, changed(next)).ConfigureAwait(true);
        return true;
    }

    /// <summary>Throws unless the revisions match.</summary>
    /// <exception cref="RevisionConflictException">The revisions differ.</exception>
    protected static void RequireRevision(long expectedRevision, long currentRevision)
    {
        if (expectedRevision != currentRevision)
        { throw new RevisionConflictException($"Expected revision {expectedRevision}; current revision is {currentRevision}.", currentRevision); }
    }
}
