using DigitalBrain.SDK.Sqlite.Sqlite;

namespace DigitalBrain.Kernel.Runtime;

public sealed class DynamicDomainRegistry(
    IDatabaseContextFactory dbFactory,
    ILogger<DynamicDomainRegistry> logger)
{
    private const string DatabaseId = "dynamic-domain-registry";
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _initialized;

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized) return;

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_initialized) return;

            using var connection = await dbFactory.OpenAsync(DatabaseId, ct).ConfigureAwait(false);
            
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS neurons (
                        fqn TEXT PRIMARY KEY,
                        source_code TEXT NOT NULL,
                        version INTEGER NOT NULL,
                        updated_at TEXT NOT NULL
                    );";
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS neuron_versions (
                        fqn TEXT NOT NULL,
                        version INTEGER NOT NULL,
                        source_code TEXT NOT NULL,
                        created_at TEXT NOT NULL,
                        PRIMARY KEY (fqn, version)
                    );";
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            _initialized = true;
            logger.LogInformation("SQLite Dynamic Domain Registry database tables initialized successfully.");
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<int> SaveNeuronAsync(string fqn, string sourceCode, CancellationToken ct)
    {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);

        using var connection = await dbFactory.OpenAsync(DatabaseId, ct).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();

        try
        {
            // Determine the next version number
            int currentVersion = 0;
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = "SELECT version FROM neurons WHERE fqn = $fqn;";
                cmd.Parameters.AddWithValue("$fqn", fqn);
                var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
                if (result != null && result != DBNull.Value)
                {
                    currentVersion = Convert.ToInt32(result);
                }
            }

            int nextVersion = currentVersion + 1;
            string now = DateTimeOffset.UtcNow.ToString("O");

            // Update main table
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = @"
                    INSERT INTO neurons (fqn, source_code, version, updated_at)
                    VALUES ($fqn, $source_code, $version, $updated_at)
                    ON CONFLICT(fqn) DO UPDATE SET
                        source_code = excluded.source_code,
                        version = excluded.version,
                        updated_at = excluded.updated_at;";
                cmd.Parameters.AddWithValue("$fqn", fqn);
                cmd.Parameters.AddWithValue("$source_code", sourceCode);
                cmd.Parameters.AddWithValue("$version", nextVersion);
                cmd.Parameters.AddWithValue("$updated_at", now);
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            // Insert version history
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = @"
                    INSERT INTO neuron_versions (fqn, version, source_code, created_at)
                    VALUES ($fqn, $version, $source_code, $created_at);";
                cmd.Parameters.AddWithValue("$fqn", fqn);
                cmd.Parameters.AddWithValue("$version", nextVersion);
                cmd.Parameters.AddWithValue("$source_code", sourceCode);
                cmd.Parameters.AddWithValue("$created_at", now);
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            transaction.Commit();
            logger.LogInformation("Saved Dynamic Neuron '{Fqn}' (Version {Version}) successfully.", fqn, nextVersion);
            return nextVersion;
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            logger.LogError(ex, "Failed to save dynamic neuron '{Fqn}' to SQLite registry.", fqn);
            throw;
        }
    }

    public async Task<DynamicNeuronRecord?> GetNeuronAsync(string fqn, CancellationToken ct)
    {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);

        using var connection = await dbFactory.OpenAsync(DatabaseId, ct).ConfigureAwait(false);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT source_code, version, updated_at FROM neurons WHERE fqn = $fqn;";
        cmd.Parameters.AddWithValue("$fqn", fqn);

        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return new DynamicNeuronRecord(
                fqn,
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetString(2)
            );
        }

        return null;
    }

    public async Task<IReadOnlyList<DynamicNeuronRecord>> GetAllNeuronsAsync(CancellationToken ct)
    {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);

        var list = new List<DynamicNeuronRecord>();
        using var connection = await dbFactory.OpenAsync(DatabaseId, ct).ConfigureAwait(false);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT fqn, source_code, version, updated_at FROM neurons;";

        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new DynamicNeuronRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetString(3)
            ));
        }

        return list;
    }

    public async Task<DynamicNeuronVersionRecord?> GetVersionAsync(string fqn, int version, CancellationToken ct)
    {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);

        using var connection = await dbFactory.OpenAsync(DatabaseId, ct).ConfigureAwait(false);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT source_code, created_at FROM neuron_versions WHERE fqn = $fqn AND version = $version;";
        cmd.Parameters.AddWithValue("$fqn", fqn);
        cmd.Parameters.AddWithValue("$version", version);

        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return new DynamicNeuronVersionRecord(
                fqn,
                version,
                reader.GetString(0),
                reader.GetString(1)
            );
        }

        return null;
    }

    public async Task RollbackNeuronAsync(string fqn, int targetVersion, CancellationToken ct)
    {
        await EnsureInitializedAsync(ct).ConfigureAwait(false);

        using var connection = await dbFactory.OpenAsync(DatabaseId, ct).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();

        try
        {
            // Verify version exists
            string? sourceCode = null;
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = "SELECT source_code FROM neuron_versions WHERE fqn = $fqn AND version = $version;";
                cmd.Parameters.AddWithValue("$fqn", fqn);
                cmd.Parameters.AddWithValue("$version", targetVersion);
                var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
                if (result == null || result == DBNull.Value)
                {
                    throw new InvalidOperationException($"Dynamic neuron '{fqn}' version {targetVersion} was not found.");
                }
                sourceCode = (string)result;
            }

            string now = DateTimeOffset.UtcNow.ToString("O");

            // Update main table back to target version (keeps version counter intact, just reverts the source/timestamp)
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = @"
                    UPDATE neurons
                    SET source_code = $source_code,
                        version = $version,
                        updated_at = $updated_at
                    WHERE fqn = $fqn;";
                cmd.Parameters.AddWithValue("$fqn", fqn);
                cmd.Parameters.AddWithValue("$source_code", sourceCode);
                cmd.Parameters.AddWithValue("$version", targetVersion);
                cmd.Parameters.AddWithValue("$updated_at", now);
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            transaction.Commit();
            logger.LogInformation("Rolled back Dynamic Neuron '{Fqn}' to Version {Version} successfully.", fqn, targetVersion);
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            logger.LogError(ex, "Failed to rollback dynamic neuron '{Fqn}' to version {Version}.", fqn, targetVersion);
            throw;
        }
    }
}

public sealed record DynamicNeuronRecord(string Fqn, string SourceCode, int Version, string UpdatedAt);
public sealed record DynamicNeuronVersionRecord(string Fqn, int Version, string SourceCode, string CreatedAt);
