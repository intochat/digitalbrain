using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DigitalBrain.Log;

// A deliberately swallowed failure stays observable: the exception object and a fixed
// description reach the log at Warning. The description is never built from payloads, tool
// arguments, secrets or the exception message.
public static partial class Swallow
{
    public static void LogSwallowed(this ILogger logger, Exception exception, string what)
    {
        GuardNotNullLogger(logger);
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(what);
        WriteSwallowed(logger, exception, what);
    }

    public static async Task TryAsync(Func<Task> work, ILogger logger, string what)
    {
        ArgumentNullException.ThrowIfNull(work);
        GuardNotNullLogger(logger);
        ArgumentNullException.ThrowIfNull(what);
#pragma warning disable CA1031
        try
        {
            await work().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogSwallowed(exception, what);
        }
#pragma warning restore CA1031
    }

    internal static bool IsNullLogger(ILogger logger)
    {
        var type = logger.GetType();
        return logger is NullLogger || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(NullLogger<>));
    }

    private static void GuardNotNullLogger(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        if (IsNullLogger(logger))
        {
            throw new ArgumentException("NullLogger is not permitted for swallowed exceptions; pass a real or fallback ILogger.", nameof(logger));
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Swallowed failure: {What}")]
    private static partial void WriteSwallowed(ILogger logger, Exception exception, string what);
}
