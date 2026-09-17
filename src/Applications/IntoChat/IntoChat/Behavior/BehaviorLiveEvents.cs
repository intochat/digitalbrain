using System.Collections.Concurrent;
using System.Threading.Channels;
using DigitalBrain.Core.Behavior;

namespace IntoChat;

// Tokens are ephemeral. The completed step is the durable result, so a dropped stream can
// be recovered by reading the run without replaying tool calls just to regenerate tokens.
public sealed class BehaviorLiveEvents : IBehaviorRunCancellation
{
    private readonly ConcurrentDictionary<string, Channel<object>> _streams = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _executions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _cancelled = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _cancellationOrder = new();

    public CancellationToken BeginExecution(string behaviorId, string runId)
    {
        var key = behaviorId + "/" + runId;
        var cancellation = _executions.GetOrAdd(key, static _ => new CancellationTokenSource());
        if (_cancelled.ContainsKey(key)) { cancellation.Cancel(); }
        return cancellation.Token;
    }

    public void EndExecution(string behaviorId, string runId)
    {
        if (_executions.TryRemove(behaviorId + "/" + runId, out var cancellation)) { cancellation.Dispose(); }
    }

    public void Cancel(string behaviorId, string runId)
    {
        var key = behaviorId + "/" + runId;
        if (_cancelled.TryAdd(key, 0))
        {
            _cancellationOrder.Enqueue(key);
            while (_cancelled.Count > 4096 && _cancellationOrder.TryDequeue(out var expired)) { _cancelled.TryRemove(expired, out _); }
        }
        if (_executions.TryGetValue(key, out var cancellation))
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
