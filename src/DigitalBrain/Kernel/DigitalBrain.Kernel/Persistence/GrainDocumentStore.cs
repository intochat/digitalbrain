using System.Text.Json;
using System.Text.Json.Serialization;
using Orleans;

namespace DigitalBrain.Kernel;

public sealed class GrainDocumentStore<T>(IGrainFactory grains, string name) : IDocumentStore<T> where T : class, new()
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.General)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private string DocumentKey(string id) => name + "/" + id;
    private string IndexKey() => name + "/.index";

    public async Task<TResult> ReadAsync<TResult>(string id, Func<T, TResult> read, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var (_, payload) = await grains.GetGrain<IDocumentGrain>(DocumentKey(id)).ReadAsync().WaitAsync(ct).ConfigureAwait(false);
        return read(Deserialize(payload));
    }

    public async Task<TResult> UpdateAsync<TResult>(string id, Func<T, TResult> update, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var grain = grains.GetGrain<IDocumentGrain>(DocumentKey(id));
        await grains.GetGrain<IDocumentIndexGrain>(IndexKey()).AddAsync(id).WaitAsync(ct).ConfigureAwait(false);
        for (var attempt = 0; attempt < 16; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var (version, payload) = await grain.ReadAsync().WaitAsync(ct).ConfigureAwait(false);
            var document = Deserialize(payload);
            var result = update(document);
            if (await grain.TryWriteAsync(version, Serialize(document)).WaitAsync(ct).ConfigureAwait(false))
            {
                return result;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(1 << attempt, 50)), ct).ConfigureAwait(false);
        }
        throw new DocumentConflictException();
    }

    public async Task<IReadOnlyList<string>> ListIdsAsync(CancellationToken ct)
    {
        var candidates = await grains.GetGrain<IDocumentIndexGrain>(IndexKey()).ListAsync().WaitAsync(ct).ConfigureAwait(false);
        var visible = new List<string>();
        foreach (var id in candidates)
        {
            var (_, payload) = await grains.GetGrain<IDocumentGrain>(DocumentKey(id)).ReadAsync().WaitAsync(ct).ConfigureAwait(false);
            if (payload is not null) { visible.Add(id); }
        }
        return visible;
    }

    private static T Deserialize(string? payload)
        => payload is null ? new T() : JsonSerializer.Deserialize<T>(payload, Json) ?? new T();

    private static string Serialize(T document) => JsonSerializer.Serialize(document, Json);
}
