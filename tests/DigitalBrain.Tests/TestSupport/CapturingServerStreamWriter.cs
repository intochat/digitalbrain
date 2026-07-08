using System.Collections.Concurrent;
using Grpc.Core;

namespace DigitalBrain.Tests.TestSupport;

public sealed class CapturingServerStreamWriter<T>(Action? afterFirstWrite = null) : IServerStreamWriter<T>
{
    private int firstWriteNotified;

    public ConcurrentQueue<T> Messages { get; } = [];
    public WriteOptions? WriteOptions { get; set; }

    public Task WriteAsync(T message)
    {
        Messages.Enqueue(message);
        if (Interlocked.Exchange(ref firstWriteNotified, 1) == 0)
        {
            afterFirstWrite?.Invoke();
        }

        return Task.CompletedTask;
    }
}
