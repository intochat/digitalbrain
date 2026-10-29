namespace DigitalBrain;

/// <summary>An error an app recorded instead of a value: where it happened, what kind it was and what it said.</summary>
/// <param name="Operation">The operation or member that recorded it.</param>
/// <param name="Kind">The exception type name, or a short app-defined kind.</param>
/// <param name="Message">The message shown to the person.</param>
/// <param name="Reason">Why the error was swallowed; null for a recorded error state.</param>
public sealed record AppError(string Operation, string Kind, string Message, string? Reason = null)
{
    /// <summary>The error <paramref name="error"/> raised in <paramref name="operation"/>.</summary>
    public static AppError From(Exception error, string operation, string? reason = null)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new AppError(operation ?? string.Empty, error.GetType().Name, error.Message, reason);
    }
}
