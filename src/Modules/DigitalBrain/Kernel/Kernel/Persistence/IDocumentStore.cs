namespace DigitalBrain.Core;

public interface IDocumentStore<T> where T : class, new()
{
    Task<TResult> ReadAsync<TResult>(string id, Func<T, TResult> read, CancellationToken ct);
    Task<TResult> UpdateAsync<TResult>(string id, Func<T, TResult> update, CancellationToken ct);
    Task<IReadOnlyList<string>> ListIdsAsync(CancellationToken ct);
}
