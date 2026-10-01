using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Sdk.Capacity;
using Npgsql;

namespace DigitalBrain.Postgres;

// Local runtime provisioning: one database per brain on the Aspire-hosted server, created over the
// admin connection run-mode hosting passes in. Publish mode never configures that connection, so
// this class is absent from production and provisioning refuses there by construction.
internal sealed class DockerPostgresProvisioner(string adminConnection, Func<string, string, CancellationToken, Task>? createDatabase = null) : ICapacityProvisioner
{
    public string Kind => PostgresCapacityKind.Kind;

    public static string DatabaseName(string brainId)
        => "brain_" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(brainId)))[..16];

    public async ValueTask<string> Ensure(CapacityScope scope, CancellationToken ct)
    {
        var database = DatabaseName(scope.BrainId);
        await (createDatabase ?? CreateDatabase)(adminConnection, database, ct);
        return PostgresCapacityKind.DatabasePrefix + database;
    }

    private static async Task CreateDatabase(string adminConnection, string database, CancellationToken ct)
    {
        try
        {
            await using var source = NpgsqlDataSource.Create(PostgresConnectionSettings.Parse(adminConnection).ConnectionString);
            await using var connection = await source.OpenConnectionAsync(ct);
            await using (var exists = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = $1", connection))
            {
                exists.Parameters.AddWithValue(database);
                if (await exists.ExecuteScalarAsync(ct) is not null) { return; }
            }
            // CREATE DATABASE cannot be parameterized; the name is hash-derived, never user input.
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", connection);
            try { await create.ExecuteNonQueryAsync(ct); }
            catch (PostgresException error) when (error.SqlState == "42P04") { } // created by a concurrent Ensure
        }
        catch (PostgresException) { throw; }
        catch (NpgsqlException) { throw new PostgresUnavailableException("Postgres is unreachable or the database connection failed."); }
    }
}

// Registered when no admin connection is configured; an inactive kind never matches a lookup.
internal sealed class InactivePostgresProvisioner : ICapacityProvisioner
{
    public string Kind => PostgresCapacityKind.Kind + ":inactive";
    public ValueTask<string> Ensure(CapacityScope scope, CancellationToken ct) => throw new CapacityUnavailableException();
}
