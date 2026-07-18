using DigitalBrain.SDK.DigitalBrain.Ui.Visuals;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace DigitalBrain.SDK.DigitalBrain.Ui.Visuals.Materials;

// Per-clientId Channel<MaterialPlan> fan-out. Mirrors FlutterPerfHintBroadcaster shape.
public sealed class MaterialPlanBroadcaster : IMaterialPlanBroadcaster
{
    readonly ConcurrentDictionary<string, List<Channel<MaterialPlan>>> _subscribers = new();
    readonly ConcurrentDictionary<string, MaterialPlan> _latest = new();

    public Task BroadcastAsync(string clientId, MaterialPlan plan)
    {
        _latest[clientId] = plan;
        if (!_subscribers.TryGetValue(clientId, out var channels))
            return Task.CompletedTask;

        List<Channel<MaterialPlan>> snapshot;
        lock (channels) snapshot = channels.ToList();

        foreach (var ch in snapshot)
            ch.Writer.TryWrite(plan);

        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<MaterialPlan> SubscribeAsync(
        string clientId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<MaterialPlan>(new UnboundedChannelOptions
        {
            SingleReader = true, SingleWriter = false,
        });

        var list = _subscribers.GetOrAdd(clientId, _ => new List<Channel<MaterialPlan>>());
        lock (list) list.Add(channel);

        // seed with the most recent plan so a fresh subscriber doesn't wait idle
        if (_latest.TryGetValue(clientId, out var seed))
            channel.Writer.TryWrite(seed);

        try
        {
            await foreach (var plan in channel.Reader.ReadAllAsync(ct))
                yield return plan;
        }
        finally
        {
            lock (list) list.Remove(channel);
            channel.Writer.TryComplete();
        }
    }
}
