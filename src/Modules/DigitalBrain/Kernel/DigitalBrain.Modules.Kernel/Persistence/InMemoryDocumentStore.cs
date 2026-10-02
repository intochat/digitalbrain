using System.Text.Json;

namespace DigitalBrain.Core;

public sealed class InMemoryDocumentStore<T> : IDocumentStore<T> where T : class, new()
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.General);
    private readonly Dictionary<string, (long Version, string Payload)> _documents = new(StringComparer.Ordinal);
    private readonly HashSet<string> _ids = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public Task<TResult> ReadAsync<TResult>(string id, Func<T, TResult> read, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        try
        {
            lock (_gate)
            {
                return Task.FromResult(read(Deserialize(_documents.TryGetValue(id, out var entry) ? entry.Payload : null)));
            }
        }
        catch (Exception error)
        {
            return Task.FromException<TResult>(error);
        }
    }

    public Task<TResult> UpdateAsync<TResult>(string id, Func<T, TResult> update, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        try
        {
            lock (_gate)
            {
                var document = Deserialize(_documents.TryGetValue(id, out var entry) ? entry.Payload : null);
                var result = update(document);
                _documents[id] = (entry.Version + 1, Serialize(document));
                _ids.Add(id);
                return Task.FromResult(result);
            }
        }
        catch (Exception error)
        {
            return Task.FromException<TResult>(error);
        }
    }

    public Task<IReadOnlyList<string>> ListIdsAsync(CancellationToken ct)
    {
        lock (_gate) { return Task.FromResult<IReadOnlyList<string>>(_ids.ToArray()); }
    }

    private static T Deserialize(string? payload)
        => payload is null ? new T() : JsonSerializer.Deserialize<T>(payload, Json) ?? new T();

    private static string Serialize(T document) => JsonSerializer.Serialize(document, Json);
}
