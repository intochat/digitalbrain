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
    private readonly List<KeyValuePair<string, long>> _stagedTallies = [];
    private long _stagedTallyCount;
    private long _committedTallyCount;
    private readonly Dictionary<string, long> _committedTallies = new(StringComparer.Ordinal);
    private JournalTally[]? _cachedTallies;
    private long _cachedTotalRecorded;

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
        var earliest = _retained.CommittedEarliestRetained;
        var gap = afterSequence + 1 < earliest;
        EnsureTalliesCached();
        var totalRecorded = _cachedTotalRecorded;

        if (afterSequence >= lastSequence || gap)
        {
            return new(lastSequence, earliest, gap, [], Snapshot(), totalRecorded);
        }

        List<SignalDelivery> deliveries = [];
        for (var index = _retained.FirstIndexAfter(afterSequence); index < _retained.CommittedCount; index++)
        {
            deliveries.Add(_retained[index].Delivery);
        }

        return new(lastSequence, earliest, gap, deliveries, null, totalRecorded);
    }

    internal void Append(SignalDelivery delivery)
    {
        var signalType = delivery.Signal.Type;

        _retained.Append(sequence => new JournalEntry(sequence, delivery));
        var recorded = RecordedOf(signalType) + 1;
        _tallies[signalType] = recorded;
        _stagedTallies.Add(new(signalType, recorded));
        _stagedTallyCount++;
    }

    internal JournalSnapshot Snapshot()
    {
        EnsureTalliesCached();

        return new(
            TotalRecorded: _cachedTotalRecorded,
            LastSequence: _retained.CommittedSequence,
            EarliestRetainedSequence: _retained.CommittedEarliestRetained,
            RetainedCount: _retained.CommittedCount,
            Tallies: _cachedTallies!);
    }

    internal JournalWindowBoundary CaptureCommitBoundary() => new(_retained.CaptureCommitBoundary(), _stagedTallyCount);

    internal void NoteCommitted(JournalWindowBoundary boundary)
    {
        if (!_retained.IsCurrent(boundary.Retained))
        {
            return;
        }

        _retained.NoteCommitted(boundary.Retained);

        var promote = (int)Math.Clamp(boundary.StagedTallyCount - _committedTallyCount, 0, _stagedTallies.Count);
        for (var index = 0; index < promote; index++)
        {
            var tally = _stagedTallies[index];
            _committedTallies[tally.Key] = tally.Value;
        }

        _stagedTallies.RemoveRange(0, promote);
        _committedTallyCount += promote;
        _cachedTallies = null;
    }

    internal void NoteReloaded()
    {
        _retained.NoteReloaded();
        _stagedTallies.Clear();
        _committedTallyCount = _stagedTallyCount;
        _committedTallies.Clear();
        foreach (var tally in _tallies)
        {
            _committedTallies[tally.Key] = tally.Value;
        }

        _cachedTallies = null;
    }

    private void EnsureTalliesCached()
    {
        if (_cachedTallies is null)
        {
            _cachedTallies = [.. _committedTallies.Select(tally => new JournalTally(tally.Key, tally.Value))];
            _cachedTotalRecorded = _cachedTallies.Sum(tally => tally.Recorded);
        }
    }

    private long RecordedOf(string signalType)
        => _tallies.TryGetValue(signalType, out var recorded) ? recorded : 0;
}

internal readonly record struct JournalWindowBoundary(JournalCommitBoundary Retained, long StagedTallyCount);
