using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DigitalBrain.Log;

internal sealed class TraceLogger : ILogger
{
    private readonly string _category;

    private TraceLogger(string category) => _category = category;

    internal static ILogger For(string category) => new TraceLogger(string.IsNullOrWhiteSpace(category) ? "DigitalBrain" : category);

    internal static ILogger Resolve(IServiceProvider? services, string category)
    {
        try
        {
            if (services?.GetService(typeof(ILoggerFactory)) is ILoggerFactory factory and not NullLoggerFactory)
            {
                var created = factory.CreateLogger(category);
                if (!Swallow.IsNullLogger(created))
                {
                    return created;
                }
            }
        }
        catch (NotSupportedException) { }
        catch (InvalidOperationException) { }

        return For(category);
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(formatter);
        var formatted = formatter(state, exception);
        var line = exception is null
            ? $"[{_category}] {formatted}"
            : $"[{_category}] {formatted} ({exception.GetType().Name}: {exception.Message})";

        switch (logLevel)
        {
            case >= LogLevel.Error:
                Trace.TraceError(line);
                break;
            case LogLevel.Warning:
                Trace.TraceWarning(line);
                break;
            default:
                Trace.TraceInformation(line);
                break;
        }
    }
}
