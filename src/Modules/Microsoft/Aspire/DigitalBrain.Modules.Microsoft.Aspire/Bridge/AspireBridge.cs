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
    // The brain turns healthy moments before the AppHost dials in, so an early command waits for it.
    internal TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(30);

    private readonly Channel<AspireBridgeCommand> _commands = Channel.CreateUnbounded<AspireBridgeCommand>();
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<AspireBridgeCommandResult>> _pending = new();
    private readonly Lock _gate = new();
    private int _connectedAppHosts;
    private TaskCompletionSource _connected = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task ExecuteAsync(string resource, string command, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        await WaitForAppHostAsync(cancellationToken).ConfigureAwait(false);
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
        lock (_gate)
        {
            if (++_connectedAppHosts == 1) { _connected.TrySetResult(); }
        }
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // The endpoint proxy in front of the brain closes streams it considers idle,
                // and every reconnect makes the AppHost re-push all resource state. A heartbeat
                // keeps the stream visibly alive; the AppHost ignores the empty command.
                var wait = _commands.Reader.WaitToReadAsync(cancellationToken).AsTask();
                var tick = await Task.WhenAny(wait, Task.Delay(TimeSpan.FromSeconds(10), cancellationToken)).ConfigureAwait(false);
                if (tick != wait)
                {
                    yield return new AspireBridgeCommand(Guid.Empty, "", "");
                    continue;
                }
                if (!await wait.ConfigureAwait(false)) { yield break; }
                while (_commands.Reader.TryRead(out var command))
                {
                    // A caller that already timed out or gave up must not have its command run later.
                    if (_pending.ContainsKey(command.Id)) { yield return command; }
                }
            }
        }
        finally
        {
            lock (_gate)
            {
                if (--_connectedAppHosts == 0) { _connected = new(TaskCreationOptions.RunContinuationsAsynchronously); }
            }
        }
    }

    private async Task WaitForAppHostAsync(CancellationToken cancellationToken)
    {
        Task connected;
        lock (_gate) { connected = _connected.Task; }
        try { await connected.WaitAsync(ConnectTimeout, cancellationToken).ConfigureAwait(false); }
        catch (TimeoutException error) { throw new InvalidOperationException("No Aspire AppHost is connected to this brain.", error); }
    }

    public void Complete(Guid id, AspireBridgeCommandResult result)
    {
        if (_pending.TryGetValue(id, out var completion)) { completion.TrySetResult(result); }
    }
}
