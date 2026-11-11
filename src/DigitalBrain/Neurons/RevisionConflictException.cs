namespace DigitalBrain;

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
