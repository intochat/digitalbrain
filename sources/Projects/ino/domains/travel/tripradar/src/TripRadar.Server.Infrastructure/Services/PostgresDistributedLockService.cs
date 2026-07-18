using System.Data;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;
using TripRadar.Server.Application.Contracts.Services;

namespace TripRadar.Server.Infrastructure.Services;

public sealed class PostgresDistributedLockService(
    string connectionString,
    ILogger<PostgresDistributedLockService> logger) : IDistributedLockService
{
    private const int PollIntervalMilliseconds = 100;
    private readonly string _connectionString = !string.IsNullOrWhiteSpace(connectionString)
        ? connectionString
        : throw new InvalidOperationException("Connection string 'db' is required for PostgreSQL distributed locking.");

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

        if (string.IsNullOrWhiteSpace(lockKey))
        {
            throw new ArgumentException("Lock key must be provided.", nameof(lockKey));
        }

        var advisoryLockKey = ComputeAdvisoryLockKey(lockKey);
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        try
        {
            var acquired = waitTimeout <= TimeSpan.Zero
                ? await TryAcquireAsync(connection, advisoryLockKey, cancellationToken)
                : await TryAcquireWithWaitAsync(connection, advisoryLockKey, waitTimeout, cancellationToken);

            if (!acquired)
            {
                await connection.DisposeAsync();
                return null;
            }

            return new PostgresLockHandle(connection, advisoryLockKey, lockKey, logger);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static long ComputeAdvisoryLockKey(string lockKey)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(lockKey));
        return BitConverter.ToInt64(hashBytes, 0);
    }

    private static async Task<bool> TryAcquireAsync(
        NpgsqlConnection connection,
        long advisoryLockKey,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@lock_key)", connection);
        command.Parameters.AddWithValue("lock_key", advisoryLockKey);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is true;
    }

    private static async Task<bool> TryAcquireWithWaitAsync(
        NpgsqlConnection connection,
        long advisoryLockKey,
        TimeSpan waitTimeout,
        CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.StartNew();
        while (true)
        {
            if (await TryAcquireAsync(connection, advisoryLockKey, cancellationToken))
            {
                return true;
            }

            var remaining = waitTimeout - startedAt.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                return false;
            }

            var delay = TimeSpan.FromMilliseconds(Math.Min(PollIntervalMilliseconds, remaining.TotalMilliseconds));
            await Task.Delay(delay, cancellationToken);
        }
    }

    private sealed class PostgresLockHandle(
        NpgsqlConnection connection,
        long advisoryLockKey,
        string lockKey,
        ILogger<PostgresDistributedLockService> logger)
        : IAsyncDisposable
    {
        private int _released;

        public async ValueTask DisposeAsync()
        {
            await ReleaseAsync(CancellationToken.None);
        }

        private async Task ReleaseAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _released, 1) != 0)
            {
                return;
            }

            try
            {
                if (connection.State != ConnectionState.Closed)
                {
                    await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(@lock_key)", connection);
                    command.Parameters.AddWithValue("lock_key", advisoryLockKey);
                    await command.ExecuteScalarAsync(cancellationToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to release PostgreSQL distributed lock {LockKey}", lockKey);
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }
    }
}
