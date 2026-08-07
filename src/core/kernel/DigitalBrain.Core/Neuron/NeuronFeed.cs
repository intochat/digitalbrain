using DigitalBrain.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.Core;

internal sealed class NeuronFeed
{
    private const int MaxRetainedEntries = 512;
    private const int MaxRetainedBytes = 512 * 1024;

    private readonly IDurableList<byte[]> _retained;
    private readonly IDurableDictionary<string, long> _tallies;
    private readonly IDurableValue<long> _lastSequence;
    private readonly Serializer<JournalEntry> _entries;

    internal NeuronFeed(IServiceProvider services, string name)
    {
        _retained = services.GetRequiredKeyedService<IDurableList<byte[]>>(name);
        _tallies = services.GetRequiredKeyedService<IDurableDictionary<string, long>>($"{name}.tally");
        _lastSequence = services.GetRequiredKeyedService<IDurableValue<long>>($"{name}.sequence");
        _entries = services.GetRequiredService<Serializer<JournalEntry>>();
    }

    internal JournalRead Read(long afterSequence)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(afterSequence);

        var lastSequence = _lastSequence.Value;

        if (afterSequence > lastSequence
            || (afterSequence < lastSequence && afterSequence < EarliestRetainedSequence() - 1))
        {
            return new(lastSequence, [], Snapshot());
        }

        var firstIndex = (int)(afterSequence - EarliestRetainedSequence() + 1);

        return new(
            ResumeSequence: lastSequence,
            Delta: [.. _retained.Skip(firstIndex).Select(_entries.Deserialize).Select(entry => entry.Delivery)],
            ResetSnapshot: null);
    }

    internal long NextSequence => _lastSequence.Value + 1;

    internal NeuronFeedCheckpoint Checkpoint() => new(
        [.. _retained],
        _tallies.ToDictionary(entry => entry.Key, entry => entry.Value),
        _lastSequence.Value);

    internal void Append(SynapseDelivery delivery)
    {
        var sequence = _lastSequence.Value + 1;
        var synapseType = delivery.Synapse.GetType().FullName!;

        _lastSequence.Value = sequence;
        _retained.Add(_entries.SerializeToArray(new JournalEntry(sequence, delivery)));
        _tallies[synapseType] = RecordedOf(synapseType) + 1;

        Compact();
    }

    internal JournalSnapshot Snapshot() => new(
        TotalRecorded: _tallies.Sum(tally => tally.Value),
        LastSequence: _lastSequence.Value,
        EarliestRetainedSequence: EarliestRetainedSequence(),
        RetainedCount: _retained.Count,
        Tallies: [.. _tallies.Select(tally => new JournalTally(tally.Key, tally.Value))]);

    internal void Restore(NeuronFeedCheckpoint checkpoint)
    {
        while (_retained.Count > 0)
        {
            _retained.RemoveAt(_retained.Count - 1);
        }

        foreach (var entry in checkpoint.Retained)
        {
            _retained.Add(entry);
        }

        foreach (var key in _tallies.Select(entry => entry.Key).ToArray())
        {
            _tallies.Remove(key);
        }

        foreach (var tally in checkpoint.Tallies)
        {
            _tallies[tally.Key] = tally.Value;
        }

        _lastSequence.Value = checkpoint.LastSequence;
    }

    private long EarliestRetainedSequence()
        => _retained.Count == 0 ? _lastSequence.Value + 1 : _lastSequence.Value - _retained.Count + 1;

    private long RecordedOf(string synapseType)
        => _tallies.TryGetValue(synapseType, out var recorded) ? recorded : 0;

    private void Compact()
    {
        var retainedBytes = _retained.Sum(entry => (long)entry.Length);

        while (_retained.Count > MaxRetainedEntries
            || (retainedBytes > MaxRetainedBytes && _retained.Count > 1))
        {
            retainedBytes -= _retained[0].Length;
            _retained.RemoveAt(0);
        }
    }
}
