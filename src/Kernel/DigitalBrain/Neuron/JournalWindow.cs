using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Signals;
using Orleans.Journaling;
using Orleans.Serialization;
using Orleans.Serialization.Session;

namespace DigitalBrain.Core;

// One direction of a neuron's traffic journal. Retention bounds recent deliveries;
// sequence and lifetime tallies survive compaction.
internal sealed class JournalWindow
{
    private readonly BoundedJournal<JournalEntry> _retained;
    private readonly IDurableDictionary<string, long> _tallies;
    private readonly IDurableValue<long> _lastSequence;

    internal JournalWindow(
        IDurableList<byte[]> retained,
        IDurableDictionary<string, long> tallies,
        IDurableValue<long> lastSequence,
        Serializer<JournalEntry> entries,
        SerializerSessionPool sessions)
    {
        ArgumentNullException.ThrowIfNull(retained);
        ArgumentNullException.ThrowIfNull(tallies);
        ArgumentNullException.ThrowIfNull(lastSequence);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(sessions);

        _retained = new(retained, entries, sessions);
        _tallies = tallies;
        _lastSequence = lastSequence;
    }

    internal long NextSequence => _lastSequence.Value + 1;

    internal JournalRead Read(long afterSequence)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(afterSequence);

        var lastSequence = _lastSequence.Value;
        var earliest = EarliestRetainedSequence();
        var gap = afterSequence + 1 < earliest;

        if (afterSequence > lastSequence || gap)
        {
            return new(lastSequence, earliest, gap, [], Snapshot());
        }

        var firstIndex = (int)(afterSequence - earliest + 1);
        List<SignalDelivery> deliveries = [];
        for (var index = firstIndex; index < _retained.Count; index++)
        {
            deliveries.Add(_retained[index].Delivery);
        }

        return new(lastSequence, earliest, gap, deliveries, null);
    }

    internal void Append(SignalDelivery delivery)
    {
        var sequence = _lastSequence.Value + 1;
        var signalType = TallyKeyFor(delivery);

        _lastSequence.Value = sequence;
        _retained.Append(new JournalEntry(sequence, delivery));
        _tallies[signalType] = RecordedOf(signalType) + 1;
    }

    internal JournalSnapshot Snapshot() => new(
        TotalRecorded: _tallies.Sum(tally => tally.Value),
        LastSequence: _lastSequence.Value,
        EarliestRetainedSequence: EarliestRetainedSequence(),
        RetainedCount: _retained.Count,
        Tallies: [.. _tallies.Select(tally => new JournalTally(tally.Key, tally.Value))]);

    private long EarliestRetainedSequence()
        => _retained.Count == 0 ? _lastSequence.Value + 1 : _lastSequence.Value - _retained.Count + 1;

    private long RecordedOf(string signalType)
        => _tallies.TryGetValue(signalType, out var recorded) ? recorded : 0;

    private static string TallyKeyFor(SignalDelivery delivery) => delivery.Signal.Type;
}
