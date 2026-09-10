using Orleans.Journaling;
using Orleans.Serialization;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Session;

namespace DigitalBrain.Core;

internal sealed class BoundedJournal<T>(
    IDurableList<byte[]> retained,
    Serializer<T> entries,
    SerializerSessionPool sessions)
{
    private const int MaxRetainedEntries = 512;
    private const int MaxRetainedBytes = 512 * 1024;

    internal int Count => retained.Count;

    internal T this[int index]
    {
        get
        {
            using var session = sessions.GetSession();
            var reader = Reader.Create(retained[index], session);
            return entries.Deserialize(ref reader)!;
        }
    }

    internal void Append(T entry)
    {
        retained.Add(entries.SerializeToArray(entry));
        var retainedBytes = retained.Sum(encoded => (long)encoded.Length);
        while (retained.Count > MaxRetainedEntries
            || (retainedBytes > MaxRetainedBytes && retained.Count > 1))
        {
            retainedBytes -= retained[0].Length;
            retained.RemoveAt(0);
        }
    }
}
