using Npgsql;

namespace DigitalBrain.SDK.Postgres;

public interface INpgsqlConnectionFactory
{
    Task<NpgsqlConnection> OpenConnectionAsync(string databaseId, CancellationToken cancellationToken = default);
}
