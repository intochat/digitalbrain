namespace Brain.Modules.Memory.Contracts;

public interface IMemoryGrain : IGrainWithStringKey
{
    Task<MemoryMutationResult> StoreAsync(StoreMemoryRequest request);

    Task<MemorySearchResult> SearchAsync(string query, int limit);

    Task<MemoryMutationResult> RemoveAsync(string key, string idempotencyKey);
}
