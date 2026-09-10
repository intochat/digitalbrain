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
    private JournalTally[] _committedTallies = [];

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

        _retained = new(retained, lastSequence, entries, sessions);
        _tallies = tallies;
    }

    internal long NextSequence => _retained.LastSequence + 1;

    internal JournalRead Read(long afterSequence)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(afterSequence);

        var lastSequence = _retained.CommittedSequence;
        var earliest = _retained.EarliestRetained;
        var gap = afterSequence + 1 < earliest;

        if (afterSequence >= lastSequence || gap)
        {
            return new(lastSequence, earliest, gap, [], Snapshot());
        }

        List<SignalDelivery> deliveries = [];
        for (var index = _retained.FirstIndexAfter(afterSequence); index < _retained.CommittedCount; index++)
        {
            deliveries.Add(_retained[index].Delivery);
        }

        return new(lastSequence, earliest, gap, deliveries, null);
    }

    internal void Append(SignalDelivery delivery)
    {
        var signalType = TallyKeyFor(delivery);

        _retained.Append(sequence => new JournalEntry(sequence, delivery));
        _tallies[signalType] = RecordedOf(signalType) + 1;
    }

    internal JournalSnapshot Snapshot() => new(
        TotalRecorded: _committedTallies.Sum(tally => tally.Recorded),
        LastSequence: _retained.CommittedSequence,
        EarliestRetainedSequence: _retained.EarliestRetained,
        RetainedCount: _retained.CommittedCount,
        Tallies: _committedTallies);

    internal void NoteCommitted()
    {
        _retained.NoteCommitted();
        _committedTallies = [.. _tallies.Select(tally => new JournalTally(tally.Key, tally.Value))];
    }

    private long RecordedOf(string signalType)
        => _tallies.TryGetValue(signalType, out var recorded) ? recorded : 0;

    private static string TallyKeyFor(SignalDelivery delivery) => delivery.Signal.Type;
}
