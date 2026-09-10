using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using Orleans.Journaling;
using Orleans.Serialization;
using Orleans.Serialization.Session;

namespace DigitalBrain.Core;

internal sealed class CommandJournal
{
    private readonly BoundedJournal<CommandRecord> _retained;
    private readonly IDurableValue<long> _lastSequence;

    internal CommandJournal(
        IDurableList<byte[]> retained,
        IDurableValue<long> lastSequence,
        Serializer<CommandRecord> records,
        SerializerSessionPool sessions)
    {
        ArgumentNullException.ThrowIfNull(retained);
        ArgumentNullException.ThrowIfNull(lastSequence);
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(sessions);
        _retained = new(retained, records, sessions);
        _lastSequence = lastSequence;
    }

    internal long CommittedSequence => _lastSequence.Value; // A5 makes this a real committed cursor.

    internal long EarliestRetained => _retained.Count == 0 ? CommittedSequence + 1 : CommittedSequence - _retained.Count + 1;

    internal CommandRecord Append(CommandRecord record)
    {
        var next = CommittedSequence + 1;
        var stored = record with { Sequence = next };
        _retained.Append(stored);
        _lastSequence.Value = next;
        return stored;
    }

    internal CommandJournalRead Read(long afterSequence)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(afterSequence);
        var earliest = EarliestRetained;
        List<CommandRecord> delta = [];
        for (var index = 0; index < _retained.Count; index++)
        {
            var record = _retained[index];
            if (record.Sequence > afterSequence)
            {
                delta.Add(record);
            }
        }

        return new(CommittedSequence, earliest, afterSequence + 1 < earliest, delta);
    }

    internal bool HasTerminalRecord(CommandId id, int incarnation)
    {
        for (var index = 0; index < _retained.Count; index++)
        {
            var record = _retained[index];
            if (record.Id == id && record.Incarnation == incarnation
                && record.Phase is CommandPhase.Completed or CommandPhase.Failed or CommandPhase.Unknown)
            {
                return true;
            }
        }

        return false;
    }
}
