using Ino.Core;

namespace Ino.Testing;

public sealed class InoTestCapture : IInoTestCapture
{
    private readonly List<CaptureEntry> _entries = [];
    private readonly Lock _lock = new();

    public void Record(Type grainType, ISynapse synapse)
    {
        lock (_lock)
        {
            _entries.Add(new CaptureEntry(grainType, synapse.GetType(), synapse, DateTimeOffset.UtcNow));
        }
    }

    public IReadOnlyList<CaptureEntry> Entries
    {
        get { lock (_lock) return _entries.ToArray(); }
    }

    public void Clear()
    {
        lock (_lock) _entries.Clear();
    }
}
