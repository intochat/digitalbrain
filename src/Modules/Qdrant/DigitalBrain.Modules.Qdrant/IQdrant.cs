namespace DigitalBrain.Qdrant;

// Callers write points under their own keys; the store hashes each key to a stable point id,
// which is what Scroll hands back. Keep anything a reader needs in the payload.
public sealed record VectorPoint(string Id, float[] Vector, IReadOnlyDictionary<string, string> Payload);

public sealed record VectorHit(double Score, IReadOnlyDictionary<string, string> Payload);

public sealed record VectorPage(IReadOnlyList<VectorPoint> Points, string? NextCursor);

// Reads from a collection that does not exist yet return nothing instead of failing.
public interface IQdrant
{
    Task Upsert(string collection, IReadOnlyList<VectorPoint> points, CancellationToken cancellationToken);

    Task<IReadOnlyList<VectorHit>> Search(string collection, float[] vector, int take,
        IReadOnlyDictionary<string, string> mustMatch, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, string>?> ReadPayload(string collection, string id, CancellationToken cancellationToken);

    Task<VectorPage> Scroll(string collection, IReadOnlyDictionary<string, string> mustMatch, string? cursor, int limit,
        CancellationToken cancellationToken);

    Task Delete(string collection, IReadOnlyList<string> ids, CancellationToken cancellationToken);

    Task<long> DeleteWhere(string collection, IReadOnlyDictionary<string, string> mustMatch, CancellationToken cancellationToken);
}
