namespace TripRadar.Server.Application.Contracts.Services;

/// <summary>
/// Provides distributed locking capabilities to prevent concurrent access to shared resources
/// across multiple application instances.
/// </summary>
public interface IDistributedLockService
{
    /// <summary>
    /// Acquires a distributed lock for the given key.
    /// </summary>
    /// <param name="lockKey">The unique key identifying the lock.</param>
    /// <param name="lockTimeout">Lease duration hint for lock implementations.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A disposable lock handle if acquired, null if lock could not be acquired.</returns>
    Task<IAsyncDisposable?> TryAcquireLockAsync(string lockKey, TimeSpan lockTimeout, CancellationToken cancellationToken = default);

    /// <summary>
    /// Acquires a distributed lock, waiting up to the specified duration.
    /// </summary>
    /// <param name="lockKey">The unique key identifying the lock.</param>
    /// <param name="lockTimeout">Lease duration hint for lock implementations.</param>
    /// <param name="waitTimeout">Maximum time to wait for the lock.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A disposable lock handle if acquired, null if lock could not be acquired within wait timeout.</returns>
    Task<IAsyncDisposable?> TryAcquireLockAsync(string lockKey, TimeSpan lockTimeout, TimeSpan waitTimeout, CancellationToken cancellationToken = default);
}
