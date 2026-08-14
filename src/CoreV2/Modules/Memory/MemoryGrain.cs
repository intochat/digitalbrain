using Brain.Modules.Memory.Contracts;
using Orleans.Runtime;

namespace Brain.Modules.Memory;

public sealed class MemoryGrain(
    [PersistentState("memory", "Default")]
    IPersistentState<MemoryState> state) : Grain, IMemoryGrain
{
    private readonly IPersistentState<MemoryState> _state = state;

    public async Task<MemoryMutationResult> StoreAsync(StoreMemoryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Key)
            || string.IsNullOrWhiteSpace(request.Text)
            || string.IsNullOrWhiteSpace(request.Principal)
            || string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new ArgumentException("A memory record requires key, text, principal, and idempotency key.");
        }

        EnsureIdentity();
        if (_state.State.ProcessedRequests.Add(request.IdempotencyKey))
        {
            _state.State.Records[request.Key] = new MemoryRecord(
                request.Key,
                request.Text.Trim(),
                request.Principal);
            await _state.WriteStateAsync();
        }

        return new MemoryMutationResult(_state.State.Namespace, request.Key, "stored");
    }

    public Task<MemorySearchResult> SearchAsync(string query, int limit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        EnsureIdentity();
        var matches = _state.State.Records.Values
            .Where(record => record.Text.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(static record => record.Key, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();
        return Task.FromResult(new MemorySearchResult(_state.State.Namespace, matches));
    }

    public async Task<MemoryMutationResult> RemoveAsync(string key, string idempotencyKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        EnsureIdentity();
        var removed = false;
        if (_state.State.ProcessedRequests.Add(idempotencyKey))
        {
            removed = _state.State.Records.Remove(key);
            await _state.WriteStateAsync();
        }

        return new MemoryMutationResult(
            _state.State.Namespace,
            key,
            removed ? "removed" : "missing");
    }

    private void EnsureIdentity()
    {
        if (_state.State.Namespace.Length != 0)
        {
            return;
        }

        var key = this.GetPrimaryKeyString();
        _state.State.Namespace = key[(key.IndexOf(':', StringComparison.Ordinal) + 1)..];
    }
}
