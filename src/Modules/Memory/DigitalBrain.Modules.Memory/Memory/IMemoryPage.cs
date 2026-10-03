using DigitalBrain;
using DigitalBrain.Contracts;

namespace DigitalBrain.Memory;

[Alias("memory.page"), Orleans.Metadata.DefaultGrainType("memory.page")]
internal interface IMemoryPage : INeuron
{
    Task<bool> Put(VectorMemoryEntry entry);
    Task<bool> Remove(string name, string @namespace, string key);
    Task<int> Count();
    Task<bool> Contains(string key);
    Task<bool> HasCapacity();
    Task Clear();
    Task<MemoryMatch[]> Search(float[] vector, int limit, Dictionary<string, string> tags);
    Task<MemoryIndexResult> Rebuild();
}
