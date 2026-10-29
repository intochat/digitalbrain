using System.Collections.Concurrent;
using System.Threading.Channels;
using DigitalBrain.Capabilities;
using DigitalBrain.Runtime.Orleans.Capabilities;
using DigitalBrain.Runtime.Orleans.Synapses;
using DigitalBrain.Spi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DigitalBrain.Runtime.Orleans.Signalling;

/// <summary>
/// The silo singleton behind <see cref="IClusterListeners"/> (04-19). Every host grain offers each durable journal append here
/// (the publish tap in <c>HostNeuronState</c>, beside the <see cref="SignalPublisher"/> reference), so a silo hears every signal
/// published by the neurons it hosts. Registered on every silo at host start, the listeners together hear every brain once.
/// Handlers run on one pump off the grain scheduler, in publish order, so a handler that calls back into the publisher cannot
/// deadlock its turn. In-memory and best effort: a signal appended while the silo stops is not replayed.
/// </summary>
internal sealed partial class ClusterListeners(IServiceProvider services) : IClusterListeners, IAsyncDisposable
{
    private readonly ConcurrentDictionary<(string Listener, Type Signal), Handler> _handlers = new();
    private readonly Channel<(NeuronId Publisher, Signal Signal, CapabilityCaller? Caller)> _queue = Channel.CreateUnbounded<(NeuronId, Signal, CapabilityCaller?)>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _stopping = new();
    private readonly ILogger _log = services.GetService<ILoggerFactory>()?.CreateLogger<ClusterListeners>() ?? NullLogger<ClusterListeners>.Instance;
    private Task? _pump;
    private long _failures;

    /// <summary>Handlers that threw.</summary>
    internal long Failures => Interlocked.Read(ref _failures);

    /// <summary>Registered handlers.</summary>
    internal int Count => _handlers.Count;

    public Task<IAsyncDisposable> Listen<TSignal>(string listener, Func<NeuronId, TSignal, CancellationToken, Task> handler) where TSignal : Signal
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(listener);
        ArgumentNullException.ThrowIfNull(handler);
        if (!services.GetRequiredService<JournalPayloads>().Types.TryAliasOf(typeof(TSignal), out _))
        {
            throw new ArgumentException($"{typeof(TSignal).Name} is not a registered [Signal] type.", nameof(handler));
        }
        var key = (listener, typeof(TSignal));
        var entry = new Handler(typeof(TSignal), (publisher, signal, ct) => handler(publisher, (TSignal)signal, ct));
        _handlers[key] = entry;
        lock (_handlers)
        {
            _pump ??= Task.Factory.StartNew(Pump, CancellationToken.None, TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach, TaskScheduler.Default).Unwrap();
        }
        return Task.FromResult<IAsyncDisposable>(new Registration(this, key, entry));
    }

    /// <summary>Offers a durable append; signals no handler takes are dropped at once.</summary>
    internal void Offer(NeuronId publisher, IReadOnlyList<Signal> signals)
    {
        if (_handlers.IsEmpty) { return; }
        // The pump is not the publishing call. The caller's stamp is on this turn and does not flow onto that task.
        var caller = RuntimeCallerContext.Current;
        foreach (var signal in signals)
        {
            if (_handlers.Values.Any(item => item.Type.IsInstanceOfType(signal))) { _queue.Writer.TryWrite((publisher, signal, caller)); }
        }
    }

    private async Task Pump()
    {
        try
        {
            await foreach (var (publisher, signal, caller) in _queue.Reader.ReadAllAsync(_stopping.Token).ConfigureAwait(false))
            {
                using var scope = caller is null ? null : RuntimeCallerContext.Stamp(caller);
                foreach (var handler in _handlers.Values.Where(item => item.Type.IsInstanceOfType(signal)).ToArray())
                {
                    try { await handler.Run(publisher, signal, _stopping.Token).ConfigureAwait(false); }
                    catch (Exception error) when (error is not OutOfMemoryException)
                    {
                        Interlocked.Increment(ref _failures);
                        LogListenerFailed(_log, error, publisher.ToString(), signal.GetType().Name);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested) { }
    }

    public async ValueTask DisposeAsync()
    {
        _queue.Writer.TryComplete();
        await _stopping.CancelAsync().ConfigureAwait(false);
        if (_pump is { } pump) { await pump.ConfigureAwait(false); }
        _stopping.Dispose();
    }

    [LoggerMessage(EventId = 4139, EventName = "ClusterListenerFailed", Level = LogLevel.Warning, Message = "A cluster listener failed on {Signal} of {Publisher}; the other listeners still run.")]
    private static partial void LogListenerFailed(ILogger logger, Exception error, string publisher, string signal);

    private sealed record Handler(Type Type, Func<NeuronId, Signal, CancellationToken, Task> Run);

    private sealed class Registration(ClusterListeners owner, (string, Type) key, Handler handler) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            owner._handlers.TryRemove(new KeyValuePair<(string, Type), Handler>(key, handler));
            return ValueTask.CompletedTask;
        }
    }
}
