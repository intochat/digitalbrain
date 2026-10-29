using System.Collections.Concurrent;
using System.Collections.Immutable;
using DigitalBrain.Runtime.Orleans.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DigitalBrain.Runtime.Orleans.Signalling;

/// <summary>The silo singleton behind <see cref="IBrainListeners"/>: typed handlers per brain, dispatched by the listener neuron.</summary>
internal sealed partial class BrainListeners(IServiceProvider services) : IBrainListeners
{
    /// <summary>The neuron type of listener neurons.</summary>
    internal const string NeuronType = "runtime.listener";

    private readonly ConcurrentDictionary<string, ImmutableArray<Handler>> _handlers = new(StringComparer.Ordinal);
    private readonly ILogger _log = services.GetService<ILoggerFactory>()?.CreateLogger<BrainListeners>() ?? NullLogger<BrainListeners>.Instance;
    private long _failures;

    /// <summary>Handlers that threw.</summary>
    internal long Failures => Interlocked.Read(ref _failures);

    /// <summary>The listener neuron of <paramref name="brain"/>.</summary>
    internal static NeuronId ListenerOf(BrainId brain) => new(brain, NeuronType, brain.Value);

    public async Task<IAsyncDisposable> Listen<TSignal>(BrainId brain, Func<TSignal, CancellationToken, Task> handler) where TSignal : Signal
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (!services.GetRequiredService<JournalPayloads>().Types.TryAliasOf(typeof(TSignal), out var alias))
        {
            throw new ArgumentException($"{typeof(TSignal).Name} is not a registered [Signal] type.", nameof(handler));
        }
        var entry = new Handler(typeof(TSignal), (signal, ct) => handler((TSignal)signal, ct));
        _handlers.AddOrUpdate(brain.Value, _ => [entry], (_, list) => list.Add(entry));
        var registration = new Registration(this, brain.Value, entry);
        try { await NeuronHosting.Get<IBrainListenerHost>(services, ListenerOf(brain)).Listen([alias]).ConfigureAwait(false); }
        catch
        {
            await registration.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        return registration;
    }

    /// <summary>Handlers registered on this silo for <paramref name="brain"/>.</summary>
    internal int Count(BrainId brain) => _handlers.TryGetValue(brain.Value, out var list) ? list.Length : 0;

    /// <summary>Hands <paramref name="signal"/> to every handler of <paramref name="brain"/> that takes its type, one after another.</summary>
    internal async Task Dispatch(BrainId brain, Signal signal)
    {
        if (!_handlers.TryGetValue(brain.Value, out var list)) { return; }
        foreach (var handler in list.Where(item => item.Type.IsInstanceOfType(signal)))
        {
            try { await handler.Run(signal, CancellationToken.None).ConfigureAwait(true); }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                Interlocked.Increment(ref _failures);
                LogListenerFailed(_log, error, brain.Value, signal.GetType().Name);
            }
        }
    }

    private void Remove(string brain, Handler handler)
    {
        while (_handlers.TryGetValue(brain, out var list))
        {
            var next = list.Remove(handler);
            if (next.Length == list.Length) { return; }
            if (next.IsEmpty ? _handlers.TryRemove(new KeyValuePair<string, ImmutableArray<Handler>>(brain, list)) : _handlers.TryUpdate(brain, next, list)) { return; }
        }
    }

    [LoggerMessage(EventId = 4130, EventName = "BrainListenerFailed", Level = LogLevel.Warning, Message = "A listener of brain {Brain} failed on {Signal}; the other listeners still run.")]
    private static partial void LogListenerFailed(ILogger logger, Exception error, string brain, string signal);

    private sealed record Handler(Type Type, Func<Signal, CancellationToken, Task> Run);

    private sealed class Registration(BrainListeners owner, string brain, Handler handler) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            owner.Remove(brain, handler);
            return ValueTask.CompletedTask;
        }
    }
}
