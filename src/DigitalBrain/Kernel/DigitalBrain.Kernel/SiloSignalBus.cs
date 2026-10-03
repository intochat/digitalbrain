using System.Threading.Channels;
using DigitalBrain;

namespace DigitalBrain.Kernel;

internal sealed class SiloSignalBus
{
    private readonly Lock _gate = new();
    private readonly HashSet<Action<Signal>> _subscribers = [];
    public LocalSignalHub.Subscription<T> Subscribe<T>() where T : Signal
    {
        var channel = Channel.CreateBounded<T>(new BoundedChannelOptions(1024) { SingleReader = true, AllowSynchronousContinuations = false });
        void Close() { lock (_gate) { _subscribers.Remove(Receive); } channel.Writer.TryComplete(); }
        void Receive(Signal signal)
        {
            if (signal is T value && !channel.Writer.TryWrite(value))
            {
                channel.Writer.TryComplete(new InvalidOperationException("Silo signal buffer overflowed."));
                Close();
            }
        }
        lock (_gate) { _subscribers.Add(Receive); }
        return new(channel.Reader, Close);
    }
    public void Publish(Signal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        Action<Signal>[] subscribers;
        lock (_gate) { subscribers = [.. _subscribers]; }
        foreach (var receive in subscribers) { receive(signal); }
    }
}
