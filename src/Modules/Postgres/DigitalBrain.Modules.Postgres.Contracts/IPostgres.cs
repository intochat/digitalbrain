using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.Postgres;

[Alias("postgres")]
[Orleans.Metadata.DefaultGrainType("postgres")]
public interface IPostgres : INeuron
{
    /// <summary>Runs one read-only SELECT with server-side caps and returns typed rows.</summary>
    [ReadOnly, Alias("query")]
    Task<PostgresQueryResult> Query(PostgresQuery query);

    /// <summary>Reads tables and columns of the configured database; omit Table for the index.</summary>
    [ReadOnly, Alias("schema")]
    Task<PostgresSchema> ReadSchema(ReadPostgresSchema query);

    [ReadOnly, Alias("connection")]
    Task<PostgresConnection> ReadConnection();
}