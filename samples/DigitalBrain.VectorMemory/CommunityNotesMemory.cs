using DigitalBrain.Client;
using DigitalBrain.Memory;

namespace DigitalBrain.VectorMemory;

public sealed class CommunityNotesMemory
{
    public const string DefaultMemoryName = "memory";

    public static VectorMemoryNamespace Notes { get; } = new("community.notes");

    public CommunityNotesMemory(string memoryName = DefaultMemoryName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memoryName);
        MemoryName = memoryName;
    }

    public string MemoryName { get; }

    public Task<VectorMemoryStored> StoreNoteAsync(
        IDigitalBrain brain,
        string key,
        string text,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
        => StoreAsync(brain, Notes, key, text, metadata, cancellationToken);

    public Task<VectorMemoryMatches> SearchNotesAsync(
        IDigitalBrain brain,
        string query,
        int limit = 5,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
        => SearchAsync(brain, Notes, query, limit, metadata, cancellationToken);

    public Task<VectorMemoryStored> StoreAsync(
        IDigitalBrain brain,
        VectorMemoryNamespace @namespace,
        string key,
        string text,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(brain);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        cancellationToken.ThrowIfCancellationRequested();

        return brain.Get<IVectorMemory>(MemoryName).SendAsync(
            new StoreVectorMemory(@namespace, key, text, metadata, Payload: null),
            cancellationToken);
    }

    public Task<VectorMemoryMatches> SearchAsync(
        IDigitalBrain brain,
        VectorMemoryNamespace @namespace,
        string query,
        int limit = 5,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(brain);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        cancellationToken.ThrowIfCancellationRequested();

        return brain.Get<IVectorMemory>(MemoryName).SendAsync(
            new SearchVectorMemory(@namespace, query, limit, metadata),
            cancellationToken);
    }
}
