using Microsoft.Extensions.Logging;

namespace DigitalBrain.Log;

// A deliberately swallowed failure stays observable: the exception object and a fixed
// description reach the log at Debug. The description is never built from payloads, tool
// arguments, secrets or the exception message.
public static partial class Swallow
{
    [LoggerMessage(Level = LogLevel.Debug, Message = "Swallowed failure: {What}")]
    public static partial void LogSwallowed(this ILogger logger, Exception exception, string what);

    public static async Task TryAsync(Func<Task> work, ILogger logger, string what)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(logger);
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
}
