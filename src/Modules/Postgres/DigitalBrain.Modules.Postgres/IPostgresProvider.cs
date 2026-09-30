using System.Diagnostics;
using System.Globalization;
using Npgsql;

namespace DigitalBrain.Postgres;

public interface IPostgresProvider
{
    Task<PostgresQueryResult> QueryAsync(string sql, int maxRows, CancellationToken cancellationToken);
    Task<PostgresSchema> ReadSchemaAsync(string? table, CancellationToken cancellationToken);
    Task<PostgresConnection> PingAsync(CancellationToken cancellationToken);
}
