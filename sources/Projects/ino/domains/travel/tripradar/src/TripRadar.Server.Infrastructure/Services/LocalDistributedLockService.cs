using System.Collections.Concurrent;
using TripRadar.Server.Application.Contracts.Services;

namespace TripRadar.Server.Infrastructure.Services;

public sealed class LocalDistributedLockService : IDistributedLockService
{
    private const int LockStripes = 64;

    private readonly SemaphoreSlim[] _locks = Enumerable.Range(0, LockStripes)
        .Select(_ => new SemaphoreSlim(1, 1))
        .ToArray();

    private readonly ConcurrentDictionary<string, LockEntry> _namedLocks = new();

    public Task<IAsyncDisposable?> TryAcquireLockAsync(
        string lockKey,
        TimeSpan lockTimeout,
        CancellationToken cancellationToken = default)
    {
        return TryAcquireLockInternalAsync(lockKey, lockTimeout, TimeSpan.Zero, cancellationToken);
    }

    public Task<IAsyncDisposable?> TryAcquireLockAsync(
        string lockKey,
        TimeSpan lockTimeout,
        TimeSpan waitTimeout,
        CancellationToken cancellationToken = default)
    {
        return TryAcquireLockInternalAsync(lockKey, lockTimeout, waitTimeout, cancellationToken);
    }

    private async Task<IAsyncDisposable?> TryAcquireLockInternalAsync(
        string lockKey,
        TimeSpan lockTimeout,
        TimeSpan waitTimeout,
        CancellationToken cancellationToken)
    {
        _ = lockTimeout;

        if (IsCacheKey(lockKey))
        {
            var semaphore = GetStripedLock(lockKey);
            var acquired = await semaphore.WaitAsync(waitTimeout, cancellationToken);
            return acquired ? new LockHandle(semaphore) : null;
        }

        var entry = _namedLocks.GetOrAdd(lockKey, _ => new LockEntry());
        Interlocked.Increment(ref entry.RefCount);

        try
        {
            var acquired = await entry.Semaphore.WaitAsync(waitTimeout, cancellationToken);
            if (!acquired)
            {
                ReleaseNamedReference(lockKey, entry);
                return null;
            }

            return new LockHandle(entry.Semaphore, () => ReleaseNamedReference(lockKey, entry));
        }
        catch
        {
            ReleaseNamedReference(lockKey, entry);
            throw;
        }
    }

    private static bool IsCacheKey(string lockKey) =>
        lockKey.StartsWith("cache:", StringComparison.OrdinalIgnoreCase);

    private SemaphoreSlim GetStripedLock(string lockKey)
    {
        var index = (lockKey.GetHashCode() & int.MaxValue) % LockStripes;
        return _locks[index];
    }

    private void ReleaseNamedReference(string lockKey, LockEntry entry)
    {
        if (Interlocked.Decrement(ref entry.RefCount) == 0 && entry.Semaphore.CurrentCount == 1)
            _namedLocks.TryRemove(new KeyValuePair<string, LockEntry>(lockKey, entry));
    }

    private sealed class LockEntry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int RefCount;
    }

    private sealed class LockHandle(SemaphoreSlim semaphore, Action? onRelease = null) : IAsyncDisposable
    {
        private int _released;

        public ValueTask DisposeAsync()
        {
            Release();
            return ValueTask.CompletedTask;
        }

        private void Release()
        {
            if (Interlocked.CompareExchange(ref _released, 1, 0) != 0)
            {
                return;
            }

            semaphore.Release();
            onRelease?.Invoke();
        }
    }
}
