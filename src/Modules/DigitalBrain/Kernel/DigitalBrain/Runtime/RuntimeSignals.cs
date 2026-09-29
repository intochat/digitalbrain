using System.Threading.Channels;
using DigitalBrain.Contracts;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Core;

// A stable source per silo. Modules are replayable; activity is bounded, live, and best effort.
public sealed class RuntimeSignals(ILogger<RuntimeSignals> logger)
{
    private readonly Lock _gate = new();
    private readonly HashSet<Channel<RuntimeSignal>> _subscriptions = [];
    private IReadOnlyList<ModuleLoaded> _modules = Array.Empty<ModuleLoaded>();

    public IReadOnlyList<ModuleLoaded> Modules { get { lock (_gate) { return _modules; } } }

    public void Publish(RuntimeSignal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        lock (_gate)
        {
            if (signal is ModuleLoaded module)
            {
                if (_modules.Any(loaded => loaded.ModuleType == module.ModuleType)) { return; }
                _modules = Array.AsReadOnly<ModuleLoaded>([.. _modules, module]);
            }
            foreach (var channel in _subscriptions)
            {
                if (!channel.Writer.TryWrite(signal))
                { logger.LogWarning("A slow runtime observer missed a {SignalType} signal.", signal.GetType().Name); }
            }
        }
    }

    public Subscription Subscribe()
    {
        lock (_gate)
        {
            var channel = Channel.CreateBounded<RuntimeSignal>(new BoundedChannelOptions(Math.Max(1024, _modules.Count))
            { SingleReader = true, FullMode = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = false });
            foreach (var module in _modules) { channel.Writer.TryWrite(module); }
            _subscriptions.Add(channel);
            return new(channel.Reader, () =>
            {
                lock (_gate) { _subscriptions.Remove(channel); channel.Writer.TryComplete(); }
            });
        }
    }

    public sealed class Subscription(ChannelReader<RuntimeSignal> reader, Action close) : IDisposable
    {
        public ChannelReader<RuntimeSignal> Reader { get; } = reader;
        public void Dispose() => close();
    }
}