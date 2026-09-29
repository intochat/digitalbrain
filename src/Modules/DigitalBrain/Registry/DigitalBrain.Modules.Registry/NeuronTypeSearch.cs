using System.Security.Cryptography;
using System.Text.Json;
using DigitalBrain.Qdrant;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Registry;

internal sealed class NeuronTypeSearch(NeuronTypes types, IQdrant? vectors, IEmbeddingGenerator<string, Embedding<float>>? embeddings) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<NeuronType>? _indexed;
    private string _collection = "";
    private string _scope = "";

    public async Task<IReadOnlyList<NeuronTypeHit>> Search(string query, int take, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(take, 1);
        if (embeddings is null || vectors is null) { throw new InvalidOperationException("Neuron vector search requires an embedding model and QdrantModule."); }
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var current = types.Read();
            if (current.Count == 0) { return []; }
            if (!ReferenceEquals(current, _indexed))
            {
                var generated = await embeddings.GenerateAsync(current.Select(type => type.Name + ": " + type.Description + "\n" + string.Join('\n', type.Methods)), cancellationToken: ct).ConfigureAwait(false);
                var data = generated.Select(embedding => embedding.Vector.ToArray()).ToArray();
                if (data.Length != current.Count || data[0].Length == 0 || data.Any(vector => vector.Length != data[0].Length))
                { throw new InvalidOperationException("The embedding provider returned an invalid set of type vectors."); }
                // Include vector contents: different models or type sets cannot mix in a shared store.
                var scope = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { Types = current, Vectors = data })));
                var collection = "neuron_types_" + data[0].Length;
                await vectors.Upsert(collection, [.. current.Select((type, index) => new VectorPoint(scope + ":" + type.Id, data[index],
                    new Dictionary<string, string> { ["scope"] = scope, ["id"] = type.Id }))], ct).ConfigureAwait(false);
                _indexed = current;
                _scope = scope;
                _collection = collection;
            }
            var queryVector = (await embeddings.GenerateAsync([query], cancellationToken: ct).ConfigureAwait(false))[0].Vector.ToArray();
            var found = await vectors.Search(_collection, queryVector, Math.Min(take, 100), new Dictionary<string, string> { ["scope"] = _scope }, ct).ConfigureAwait(false);
            var byId = current.ToDictionary(type => type.Id, StringComparer.Ordinal);
            return [.. found.Where(hit => hit.Payload.TryGetValue("id", out var id) && byId.ContainsKey(id))
                .Select(hit => new NeuronTypeHit(byId[hit.Payload["id"]], hit.Score))];
        }
        finally { _gate.Release(); }
    }

    public void Dispose() => _gate.Dispose();
}