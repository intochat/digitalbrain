using DigitalBrain.Runtime;
using DigitalBrain.Runtime.Neurons;
using Orleans.Streams;
using Orleans.Streams.Core;

namespace DigitalBrain.Runtime.Catalog;

[ImplicitStreamSubscription(Neuron.GlobalTimelineNamespace)]
public class BrainTimelineRelayGrain(ILogger<BrainTimelineRelayGrain> logger)
    : Grain, IBrainTimelineRelay, IStreamSubscriptionObserver, IAsyncObserver<Synapse>
{
    public const int MaxEntries = 500;

    readonly LinkedList<Synapse> buffer = new();
    long baseCursor;

    // Test-only ctor; the runtime ctor takes the logger via DI.
    protected BrainTimelineRelayGrain() : this(null!) { }

    public Task OnNextAsync(Synapse item, StreamSequenceToken? token = null)
    {
        buffer.AddLast(item);
        while (buffer.Count > MaxEntries)
        {
            buffer.RemoveFirst();
            baseCursor++;
        }
        return Task.CompletedTask;
    }

    public async Task OnSubscribed(IStreamSubscriptionHandleFactory handleFactory)
    {
        var handle = handleFactory.Create<Synapse>();
        try
        {
            await handle.ResumeAsync(this);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Resuming subscription with cached token failed in BrainTimelineRelayGrain. Falling back to fresh subscribe.");
            await handle.ResumeAsync(this, null);
        }
    }

    public Task OnCompletedAsync() => Task.CompletedTask;

    public Task OnErrorAsync(Exception ex)
    {
        if (ex is QueueCacheMissException || ex.GetType().FullName == "Orleans.Streams.QueueCacheMissException")
        {
            logger?.LogWarning(ex, "Transient stream cache miss in BrainTimelineRelayGrain; Orleans pulling agent will recover.");
        }
        else
        {
            logger?.LogError(ex, "BrainTimelineRelayGrain stream error");
        }
        return Task.CompletedTask;
    }

    public Task<SynapseSlice> WatchSinceAsync(long cursor)
    {
        var current = baseCursor + buffer.Count;
        var startIndex = Math.Max(0, (int)(cursor - baseCursor));
        IReadOnlyList<Synapse> deltas = startIndex >= buffer.Count
            ? Array.Empty<Synapse>()
            : buffer.Skip(startIndex).ToArray();
        return Task.FromResult(new SynapseSlice(current, deltas));
    }

    public Task<IReadOnlyList<Synapse>> SnapshotAsync(DateTimeOffset since)
    {
        IReadOnlyList<Synapse> list = since == default
            ? buffer.ToArray()
            : buffer.Where(s => s.Timestamp >= since).ToArray();
        return Task.FromResult(list);
    }

    public Task<IReadOnlyList<CatalogedNeuron>> ListSeenAsync()
    {
        var byType = new Dictionary<string, (DateTimeOffset First, DateTimeOffset Last)>(StringComparer.Ordinal);
        foreach (var s in buffer)
        {
            if (!string.IsNullOrEmpty(s.ReceiverNeuronType))
                Upsert(byType, s.ReceiverNeuronType, s.Timestamp);
            if (!string.IsNullOrEmpty(s.CallerNeuronType))
                Upsert(byType, s.CallerNeuronType, s.Timestamp);
        }
        IReadOnlyList<CatalogedNeuron> list = byType
            .Select(kv => new CatalogedNeuron(new NeuronId(kv.Key), kv.Value.First, kv.Value.Last))
            .ToArray();
        return Task.FromResult(list);
    }

    static void Upsert(IDictionary<string, (DateTimeOffset First, DateTimeOffset Last)> dict, string key, DateTimeOffset ts)
    {
        if (dict.TryGetValue(key, out var v))
        {
            var first = ts < v.First ? ts : v.First;
            var last = ts > v.Last ? ts : v.Last;
            dict[key] = (first, last);
        }
        else
        {
            dict[key] = (ts, ts);
        }
    }
}
