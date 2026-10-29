using System.Collections.Concurrent;
using System.Reflection;
using DigitalBrain.Spi;

namespace DigitalBrain.Testing;

/// <summary>
/// <see cref="IClusterListeners"/> of a <see cref="TestOS"/> (04-19): every journal append of any brain is offered to the
/// handlers, one signal at a time in append order, as tracked work (so <see cref="TestOS.Idle"/> waits for it). Registrations
/// belong to the test host and survive <see cref="TestOS.Restart"/>, as a host's start-up registration would be made again.
/// </summary>
internal sealed class TestClusterListeners(TestOS os) : IClusterListeners
{
    private readonly ConcurrentDictionary<(string Listener, Type Signal), Handler> _handlers = new();
    private readonly Lock _gate = new();
    private Task _tail = Task.CompletedTask;
    private long _failures;

    /// <summary>Handlers that threw.</summary>
    internal long Failures => Interlocked.Read(ref _failures);

    public Task<IAsyncDisposable> Listen<TSignal>(string listener, Func<NeuronId, TSignal, CancellationToken, Task> handler) where TSignal : Signal
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(listener);
        ArgumentNullException.ThrowIfNull(handler);
        if (typeof(TSignal).GetCustomAttribute<SignalAttribute>() is null)
        {
            throw new ArgumentException($"{typeof(TSignal).Name} is not a [Signal] type.", nameof(handler));
        }
        var key = (listener, typeof(TSignal));
        var entry = new Handler(typeof(TSignal), (publisher, signal, ct) => handler(publisher, (TSignal)signal, ct));
        _handlers[key] = entry;
        return Task.FromResult<IAsyncDisposable>(new Registration(this, key, entry));
    }

    internal void Appended(NeuronId publisher, IReadOnlyList<JournaledSignal> written)
    {
        if (_handlers.IsEmpty || written.Count == 0) { return; }
        var signals = written.Select(item => item.Signal).ToArray();
        lock (_gate)
        {
            var previous = _tail;
            _tail = os.Track(Run(previous, publisher, signals));
        }
    }

    private async Task Run(Task previous, NeuronId publisher, Signal[] signals)
    {
        try { await previous.ConfigureAwait(false); }
        catch (Exception error) when (error is not OutOfMemoryException) { _ = error; }
        foreach (var signal in signals)
        {
            foreach (var handler in _handlers.Values.Where(item => item.Type.IsInstanceOfType(signal)).ToArray())
            {
                try { await handler.Run(publisher, signal, CancellationToken.None).ConfigureAwait(false); }
                catch (Exception error) when (error is not OutOfMemoryException)
                {
                    _ = error;
                    Interlocked.Increment(ref _failures);
                }
            }
        }
    }

    private sealed record Handler(Type Type, Func<NeuronId, Signal, CancellationToken, Task> Run);

    private sealed class Registration(TestClusterListeners owner, (string, Type) key, Handler handler) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            owner._handlers.TryRemove(new KeyValuePair<(string, Type), Handler>(key, handler));
            return ValueTask.CompletedTask;
        }
    }
}
