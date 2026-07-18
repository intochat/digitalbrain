using Ino.Core;

namespace Ino.Testing;

public interface IInoTestCapture
{
    void Record(Type grainType, ISynapse synapse);
    IReadOnlyList<CaptureEntry> Entries { get; }
    void Clear();
}
