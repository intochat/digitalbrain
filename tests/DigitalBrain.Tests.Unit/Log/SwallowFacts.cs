using DigitalBrain.Log;
using DigitalBrain.Neurons;
using DigitalBrain.Tests.Unit.Neurons;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DigitalBrain.Tests.Unit.Log;

public sealed class SwallowFacts
{
    private sealed class RecordingLogger : ILogger
    {
        public List<(LogLevel Level, Exception? Exception, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, exception, formatter(state, exception)));
    }

    private sealed class RecordingLoggerFactory(RecordingLogger logger) : ILoggerFactory
    {
        public string? LastCategory { get; private set; }
        public void AddProvider(ILoggerProvider provider) { }

        public ILogger CreateLogger(string categoryName)
        {
            LastCategory = categoryName;
            return logger;
        }

        public void Dispose() { }
    }

    private sealed class LoggerServiceProvider(object? factory) : IServiceProvider
    {
        public object? GetService(Type serviceType)
            => serviceType == typeof(ILoggerFactory) ? factory : null;
    }

    private sealed class ThrowingServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => throw new NotSupportedException("No services wired.");
    }

    private sealed class LoggerTestNeuron : Neuron
    {
        public ILogger ReadLogger(string? category = null) => GetLogger(category);
    }

    [Fact]
    public void LogSwallowed_emits_at_warning_level_with_exception_and_context()
    {
        var logger = new RecordingLogger();
        var error = new InvalidOperationException("boom");

        logger.LogSwallowed(error, "cleanup failed");

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Same(error, entry.Exception);
        Assert.Contains("cleanup failed", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LogSwallowed_forbids_null_logger_instance()
    {
        var error = new InvalidOperationException("boom");

        Assert.Throws<ArgumentException>(() => NullLogger.Instance.LogSwallowed(error, "cleanup failed"));
    }

    [Fact]
    public void LogSwallowed_forbids_generic_null_logger()
    {
        var error = new InvalidOperationException("boom");

        Assert.Throws<ArgumentException>(() => NullLogger<SwallowFacts>.Instance.LogSwallowed(error, "cleanup failed"));
    }

    [Fact]
    public async Task TryAsync_forbids_null_logger_before_running_work()
    {
        var ran = false;

        await Assert.ThrowsAsync<ArgumentException>(() => Swallow.TryAsync(() =>
        {
            ran = true;
            return Task.CompletedTask;
        }, NullLogger.Instance, "op"));

        Assert.False(ran);
    }

    [Fact]
    public void Neuron_GetLogger_returns_host_factory_logger_when_available()
    {
        var recording = new RecordingLogger();
        var factory = new RecordingLoggerFactory(recording);
        var context = new RecordingContext(new(new BrainId("b"), "test", "k"))
        {
            RecordedServices = new LoggerServiceProvider(factory),
        };
        var neuron = new LoggerTestNeuron();
        neuron.Attach(context);

        var logger = neuron.ReadLogger("Custom.Category");
        logger.LogSwallowed(new InvalidOperationException("err"), "custom failure");

        Assert.Equal("Custom.Category", factory.LastCategory);
        Assert.Same(recording, logger);
        Assert.Single(recording.Entries);
    }

    [Fact]
    public void Neuron_GetLogger_falls_back_to_trace_logger_when_host_unavailable_or_null_factory()
    {
        var detached = new LoggerTestNeuron();
        var detachedLogger = detached.ReadLogger();
        Assert.NotNull(detachedLogger);
        detachedLogger.LogSwallowed(new InvalidOperationException("err"), "detached swallow");

        var nullContext = new RecordingContext(new(new BrainId("b"), "test", "k"))
        {
            RecordedServices = new LoggerServiceProvider(NullLoggerFactory.Instance),
        };
        var withNullFactory = new LoggerTestNeuron();
        withNullFactory.Attach(nullContext);
        var safeLogger = withNullFactory.ReadLogger();
        safeLogger.LogSwallowed(new InvalidOperationException("err"), "null factory fallback");

        var throwingContext = new RecordingContext(new(new BrainId("b"), "test", "k"))
        {
            RecordedServices = new ThrowingServiceProvider(),
        };
        var withThrowing = new LoggerTestNeuron();
        withThrowing.Attach(throwingContext);
        var fallbackLogger = withThrowing.ReadLogger();
        fallbackLogger.LogSwallowed(new InvalidOperationException("err"), "throwing services fallback");
    }
}
