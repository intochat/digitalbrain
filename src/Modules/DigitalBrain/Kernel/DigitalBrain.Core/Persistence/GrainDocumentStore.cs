using System.Text.Json;
using System.Text.Json.Serialization;
using Orleans;

namespace DigitalBrain.Core;

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
        for (var attempt = 0; attempt < 128; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var (version, payload) = await grain.ReadAsync().WaitAsync(ct).ConfigureAwait(false);
            var document = Deserialize(payload);
            var result = update(document);
            if (await grain.TryWriteAsync(version, Serialize(document)).WaitAsync(ct).ConfigureAwait(false))
            {
                await grains.GetGrain<IDocumentIndexGrain>(IndexKey()).AddAsync(id).WaitAsync(ct).ConfigureAwait(false);
                return result;
            }
        }
        throw new InvalidOperationException("The document changed repeatedly; retry the operation.");
    }

    public async Task<IReadOnlyList<string>> ListIdsAsync(CancellationToken ct)
        => await grains.GetGrain<IDocumentIndexGrain>(IndexKey()).ListAsync().WaitAsync(ct).ConfigureAwait(false);

    private static T Deserialize(string? payload)
        => payload is null ? new T() : JsonSerializer.Deserialize<T>(payload, Json) ?? new T();

    private static string Serialize(T document) => JsonSerializer.Serialize(document, Json);
}
