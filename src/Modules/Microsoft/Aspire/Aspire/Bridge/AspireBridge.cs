using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace DigitalBrain.Microsoft.Aspire;

// The AppHost dials in and reads commands from a stream, so the silo never needs the AppHost's address.
// One bridge per silo: a development cluster runs one silo next to one AppHost.
internal sealed class AspireBridge
{
    // A first start builds the resource image, which can take minutes.
    internal static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(4);

    private readonly Channel<AspireBridgeCommand> _commands = Channel.CreateUnbounded<AspireBridgeCommand>();
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<AspireBridgeCommandResult>> _pending = new();
    private int _connectedAppHosts;

    public bool IsConnected => Volatile.Read(ref _connectedAppHosts) > 0;

    public async Task ExecuteAsync(string resource, string command, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        if (!IsConnected) { throw new InvalidOperationException("No Aspire AppHost is connected to this brain."); }
        var request = new AspireBridgeCommand(Guid.NewGuid(), resource, command);
        var completion = new TaskCompletionSource<AspireBridgeCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[request.Id] = completion;
        try
        {
            await _commands.Writer.WriteAsync(request, cancellationToken).ConfigureAwait(false);
            var result = await completion.Task.WaitAsync(CommandTimeout, cancellationToken).ConfigureAwait(false);
            if (!result.Success) { throw new InvalidOperationException($"Aspire could not {command} '{resource}': {result.Message}"); }
        }
        finally { _pending.TryRemove(request.Id, out _); }
    }

    public async IAsyncEnumerable<AspireBridgeCommand> ReadCommandsAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _connectedAppHosts);
        try
        {
            await foreach (var command in _commands.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                // A caller that already timed out or gave up must not have its command run later.
                if (_pending.ContainsKey(command.Id)) { yield return command; }
            }
        }
        finally { Interlocked.Decrement(ref _connectedAppHosts); }
    }

    public void Complete(Guid id, AspireBridgeCommandResult result)
    {
        if (_pending.TryGetValue(id, out var completion)) { completion.TrySetResult(result); }
    }
}
