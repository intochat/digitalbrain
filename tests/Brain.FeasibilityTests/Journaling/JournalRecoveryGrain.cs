using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;

namespace Brain.FeasibilityTests.Journaling;

public sealed class JournalRecoveryGrain(
    [FromKeyedServices("counter")] IDurableValue<int> counter,
    [FromKeyedServices("map")] IDurableDictionary<Guid, string> map,
    [FromKeyedServices("queue")] IDurableQueue<Guid> queue,
    [FromKeyedServices("list")] IDurableList<string> list) : DurableGrain, IJournalRecoveryGrain
{
    private readonly IDurableValue<int> _counter = counter;
    private readonly IDurableDictionary<Guid, string> _map = map;
    private readonly IDurableQueue<Guid> _queue = queue;
    private readonly IDurableList<string> _list = list;

    public async Task WriteAllAsync(int counter, Guid mapKey, string mapValue, Guid queueItem, string listItem)
    {
        _counter.Value = counter;
        _map[mapKey] = mapValue;
        _queue.Enqueue(queueItem);
        _list.Add(listItem);
        await WriteStateAsync();
    }

    public Task<JournalRecoverySnapshot> ReadAllAsync()
    {
        return Task.FromResult(new JournalRecoverySnapshot(
            _counter.Value,
            new Dictionary<Guid, string>(_map),
            _queue.ToList(),
            _list.ToList()));
    }

    public async Task CommitIntentThenExternalEffectAsync(int nextCounter)
    {
        _counter.Value = nextCounter;
        await WriteStateAsync();
        JournalRecoveryExternalEffectProbe.Record();
    }
}
