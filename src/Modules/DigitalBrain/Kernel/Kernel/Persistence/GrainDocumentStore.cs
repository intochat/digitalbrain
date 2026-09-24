using System.Text.Json;
using System.Text.Json.Serialization;
using Orleans;

namespace DigitalBrain.Core;

[GenerateSerializer]
public sealed class DocumentState
{
    [Id(0)] public bool Written { get; set; }
    [Id(1)] public long Version { get; set; }
    [Id(2)] public string Payload { get; set; } = "";
}

public interface IDocumentGrain : IGrainWithStringKey
{
    Task<(long Version, string? Payload)> ReadAsync();
    Task<bool> TryWriteAsync(long expectedVersion, string payload);
}

[GrainType("brain-document")]
public sealed class DocumentGrain([PersistentState("document", "Default")] IPersistentState<DocumentState> state) : Grain, IDocumentGrain
{
    public Task<(long Version, string? Payload)> ReadAsync()
        => Task.FromResult((state.State.Version, state.State.Written ? state.State.Payload : null));

    public async Task<bool> TryWriteAsync(long expectedVersion, string payload)
    {
        var current = state.State.Written ? state.State.Version : 0;
        if (current != expectedVersion) { return false; }
        var previous = state.State;
        state.State = new DocumentState { Written = true, Version = current + 1, Payload = payload };
        try
        {
            await state.WriteStateAsync();
        }
        catch
        {
            state.State = previous;
            throw;
        }
        return true;
    }
}

[GenerateSerializer]
public sealed class DocumentIndexState
{
    [Id(0)] public HashSet<string> Ids { get; set; } = new(StringComparer.Ordinal);
}

public interface IDocumentIndexGrain : IGrainWithStringKey
{
    Task<string[]> ListAsync();
    Task AddAsync(string id);
}

[GrainType("brain-document-index")]
public sealed class DocumentIndexGrain([PersistentState("index", "Default")] IPersistentState<DocumentIndexState> state) : Grain, IDocumentIndexGrain
{
    public Task<string[]> ListAsync() => Task.FromResult(state.State.Ids.ToArray());

    public async Task AddAsync(string id)
    {
        if (state.State.Ids.Add(id)) { await state.WriteStateAsync(); }
    }
}

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
