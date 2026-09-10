using Orleans.Journaling;
using Orleans.Serialization;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Session;

namespace DigitalBrain.Core;

internal sealed class BoundedJournal<T>(
    IDurableList<byte[]> retained,
    IDurableValue<long> lastSequence,
    Serializer<T> entries,
    SerializerSessionPool sessions)
{
    private const int MaxRetainedEntries = 512;
    private const int MaxRetainedBytes = 512 * 1024;
    private long _retainedBytes;

    internal int Count => retained.Count;

    internal long LastSequence => lastSequence.Value;

    internal long CommittedSequence { get; private set; }

    // An evicted entry is gone even before its eviction commits, so readers cannot serve below the live floor.
    // CommittedSequence caps the top of the readable window.
    internal long CommittedEarliestRetained => LastSequence - Count + 1;

    internal int CommittedCount => (int)Math.Clamp(CommittedSequence - CommittedEarliestRetained + 1, 0, Count);

    internal int FirstIndexAfter(long afterSequence) => (int)Math.Clamp(afterSequence - CommittedEarliestRetained + 1, 0, Count);

    internal long CaptureCommitBoundary() => LastSequence;

    // A completed write may only ever advance the committed cursor.
    internal void NoteCommitted(long boundarySequence) => CommittedSequence = Math.Max(CommittedSequence, boundarySequence);

    internal void NoteReloaded()
    {
        CommittedSequence = LastSequence;
        _retainedBytes = retained.Sum(encoded => (long)encoded.Length);
    }

    internal T this[int index]
    {
        get
        {
            using var session = sessions.GetSession();
            var reader = Reader.Create(retained[index], session);
            return entries.Deserialize(ref reader)!;
        }
    }

    internal T Append(Func<long, T> create)
    {
        var sequence = LastSequence + 1;
        var entry = create(sequence);
        var encoded = entries.SerializeToArray(entry);
        retained.Add(encoded);
        _retainedBytes += encoded.Length;
        lastSequence.Value = sequence;
        while (retained.Count > MaxRetainedEntries
            || (_retainedBytes > MaxRetainedBytes && retained.Count > 1))
        {
            _retainedBytes -= retained[0].Length;
            retained.RemoveAt(0);
        }

        return entry;
    }
}
