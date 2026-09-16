using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using Orleans.Journaling;
using Orleans.Serialization;
using Orleans.Serialization.Session;

namespace DigitalBrain.Core;

internal sealed class CommandJournal
{
    private readonly BoundedJournal<CommandRecord> _retained;

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
        _retained = new(retained, lastSequence, records, sessions);
    }

    internal long LastSequence => _retained.LastSequence;

    internal long CommittedSequence => _retained.CommittedSequence;

    internal JournalCommitBoundary CaptureCommitBoundary() => _retained.CaptureCommitBoundary();

    internal void NoteCommitted(JournalCommitBoundary boundary) => _retained.NoteCommitted(boundary);

    internal void NoteReloaded() => _retained.NoteReloaded();

    internal CommandRecord Append(CommandRecord record) => _retained.Append(sequence => record with { Sequence = sequence });

    internal CommandJournalRead Read(long afterSequence)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(afterSequence);
        var earliest = _retained.CommittedEarliestRetained;
        var gap = afterSequence + 1 < earliest;
        List<CommandRecord> delta = [];
        if (!gap && afterSequence < CommittedSequence)
        {
            for (var index = _retained.FirstIndexAfter(afterSequence); index < _retained.CommittedCount; index++)
            {
                delta.Add(_retained[index]);
            }
        }

        return new(CommittedSequence, earliest, gap, delta);
    }

    internal HashSet<(CommandId Id, int Incarnation)> TerminalRecords()
    {
        HashSet<(CommandId Id, int Incarnation)> terminal = [];
        for (var index = 0; index < _retained.Count; index++)
        {
            var record = _retained[index];
            if (record.Phase is CommandPhase.Completed or CommandPhase.Failed or CommandPhase.Unknown)
            {
                terminal.Add((record.Id, record.Incarnation));
            }
        }

        return terminal;
    }
}
