using System.Collections.Concurrent;
using System.Threading.Channels;
using DigitalBrain;
using DigitalBrain.Contracts;
using DigitalBrain.Contracts.Signals;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;

namespace DigitalBrain.Kernel;

public sealed class LocalSignalHub(ILogger<LocalSignalHub> logger) : ILocalSignalHub
{
    private readonly ConcurrentDictionary<(GrainId, INeuronObserver), Delivery> _observers = new();
    private readonly SiloSignalBus _silo = new();
    public Subscription<T> Subscribe<T>() where T : Signal => _silo.Subscribe<T>();
    public void Publish(Signal signal) => _silo.Publish(signal);
    public sealed class Subscription<T>(ChannelReader<T> reader, Action close) : IDisposable where T : Signal
    {
        public ChannelReader<T> Reader { get; } = reader;
        public void Dispose() => close();
    }
    public void Subscribe(GrainId source, INeuronObserver observer)
    {
        var delivery = new Delivery(observer, error =>
        {
            logger.LogWarning(error, "Local signal observer failed for {Source}.", source);
            Unsubscribe(source, observer);
            if (observer is ILocalSignalFaultSink sink)
            {
                try { sink.OnError(error); }
                catch (Exception notificationError) { logger.LogWarning(notificationError, "Observer fault notification failed for {Source}.", source); }
            }
        });
        if (!_observers.TryAdd((source, observer), delivery)) { delivery.Close(); }
    }
    public void Unsubscribe(GrainId source, INeuronObserver observer)
    {
        if (_observers.TryRemove((source, observer), out var delivery)) { delivery.Close(); }
    }
    public void Publish(GrainId source, Signal signal)
    {
        _silo.Publish(signal);
        foreach (var entry in _observers)
        { if (entry.Key.Item1 == source) { entry.Value.Post(signal); } }
    }
    private sealed class Delivery
    {
        private readonly Channel<Signal> _queue = Channel.CreateBounded<Signal>(new BoundedChannelOptions(1024) { SingleReader = true, AllowSynchronousContinuations = false });
        private readonly CancellationTokenSource _lifetime = new();
        private readonly Action<Exception> _failed;
        private int _closed;
        public Delivery(INeuronObserver observer, Action<Exception> failed)
        { _failed = failed; _ = Pump(observer); }
        public void Post(Signal signal)
        {
            if (Volatile.Read(ref _closed) == 0 && !_queue.Writer.TryWrite(signal))
            { _failed(new InvalidOperationException("Local observer buffer overflowed.")); }
        }
        public void Close()
        {
            if (Interlocked.Exchange(ref _closed, 1) != 0) { return; }
            _queue.Writer.TryComplete();
            _lifetime.Cancel();
        }
        private async Task Pump(INeuronObserver observer)
        {
            try
            {
                await foreach (var signal in _queue.Reader.ReadAllAsync(_lifetime.Token))
                { await observer.OnSignalAsync(signal).WaitAsync(_lifetime.Token); }
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
            catch (Exception error) { _failed(error); }
            // Close can race a completing pump; keep the tiny CTS until this delivery is collected.
        }
    }
}
