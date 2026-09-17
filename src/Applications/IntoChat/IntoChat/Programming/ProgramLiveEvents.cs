using System.Collections.Concurrent;
using System.Threading.Channels;
using DigitalBrain.Core.Programming;

namespace IntoChat;

// Tokens are ephemeral. The completed step is the durable result, so a dropped stream can
// be recovered by reading the run without replaying tool calls just to regenerate tokens.
public sealed class ProgramLiveEvents : IProgramRunCancellation
{
    private readonly ConcurrentDictionary<string, Channel<object>> _streams = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _executions = new(StringComparer.Ordinal);

    public CancellationToken BeginExecution(string programId, string runId)
        => _executions.GetOrAdd(programId + "/" + runId, static _ => new CancellationTokenSource()).Token;

    public void EndExecution(string programId, string runId)
    {
        if (_executions.TryRemove(programId + "/" + runId, out var cancellation)) { cancellation.Dispose(); }
    }

    public void Cancel(string programId, string runId)
    {
        if (_executions.TryGetValue(programId + "/" + runId, out var cancellation))
        {
            try { cancellation.Cancel(); }
            catch (ObjectDisposedException) { }
        }
    }

    public Channel<object> Open(string runId)
    {
        var channel = Channel.CreateBounded<object>(new BoundedChannelOptions(2048)
        { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = false });
        if (!_streams.TryAdd(runId, channel)) { throw new InvalidOperationException("This run already has an active stream."); }
        return channel;
    }

    public void Publish(string runId, object item)
    {
        if (_streams.TryGetValue(runId, out var stream)) { stream.Writer.TryWrite(item); }
    }

    public void Close(string runId)
    {
        Cancel("intochat", runId);
        if (_streams.TryRemove(runId, out var stream)) { stream.Writer.TryComplete(); }
    }
}
