using DigitalBrain.Core;
using DigitalBrain.Qdrant;
using Orleans.Concurrency;

namespace DigitalBrain.Qdrant.Query;

// Door to the configured collection. Nothing is persisted on the grain: every call is served live.
[GrainType(QdrantNames.NeuronType)]
internal sealed class QdrantNeuron(IQdrantProvider provider) : Neuron, IQdrant
{
    public async Task<QdrantUpsertResult> Upsert(QdrantUpsert request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Points is null || request.Points.Count is < 1 or > QdrantUpsert.MaxPoints)
        {
            throw new QdrantQueryException($"upsert accepts between 1 and {QdrantUpsert.MaxPoints} points.");
        }

        foreach (var point in request.Points)
        {
            if (point is null || point.Vector is null || point.Vector.Length == 0)
            {
                throw new QdrantQueryException("each point needs a UUID and a vector.");
            }

            QdrantPointRules.RequirePointId(point.Id);
            QdrantPointRules.RequireFields(point.Payload);
        }

        var stored = await provider.UpsertAsync(request.Points, cancellationToken).ConfigureAwait(true);
        return new QdrantUpsertResult(stored);
    }

    [ReadOnly]
    public async Task<QdrantSearchResult> Search(QdrantSearch request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Vector is null || request.Vector.Length == 0)
        {
            throw new QdrantQueryException("vector must not be empty.");
        }

        if (request.Limit is < 1 or > QdrantSearch.MaxLimit)
        {
            throw new QdrantQueryException($"limit must be between 1 and {QdrantSearch.MaxLimit}.");
        }

        var filter = request.Filter ?? [];
        QdrantPointRules.RequireFields(filter);
        var matches = await provider.SearchAsync(request.Vector, request.Limit, filter, cancellationToken).ConfigureAwait(true);
        return new QdrantSearchResult(matches);
    }

    public async Task<QdrantDeleteResult> Delete(QdrantDelete request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Ids is null || request.Ids.Count is < 1 or > QdrantDelete.MaxIds)
        {
            throw new QdrantQueryException($"delete accepts between 1 and {QdrantDelete.MaxIds} ids.");
        }

        foreach (var id in request.Ids)
        {
            QdrantPointRules.RequirePointId(id);
        }

        var accepted = await provider.DeleteAsync(request.Ids, cancellationToken).ConfigureAwait(true);
        return new QdrantDeleteResult(accepted);
    }

    [ReadOnly]
    public Task<QdrantCollection> ReadCollection(CancellationToken cancellationToken = default)
        => provider.ReadCollectionAsync(cancellationToken);

    [ReadOnly]
    public Task<QdrantConnection> ReadConnection(CancellationToken cancellationToken = default)
        => provider.PingAsync(cancellationToken);
}
