namespace DigitalBrain.Runtime.Orleans;

/// <summary>
/// A brain journal or snapshot was written in a format this runtime does not read. The 0.7.0 format change is a clean break
/// (SAFE-01): there is no dual read; wipe the state home and start again.
/// </summary>
public sealed class BrainLogFormatException : IOException
{
    /// <summary>Creates the exception with its message.</summary>
    public BrainLogFormatException(string message) : base(message) { }

    /// <summary>Creates the exception with its message and cause.</summary>
    public BrainLogFormatException(string message, Exception inner) : base(message, inner) { }

    /// <summary>Creates the exception.</summary>
    public BrainLogFormatException() { }

    internal static BrainLogFormatException Predates(string found, string directory)
        => new($"SAFE-01: journal format {found} predates {BrainLogFormat.Json}; wipe the state home (brain store {directory}) and start again. "
            + "Old journals are not read (clean break, no migration).");
}
