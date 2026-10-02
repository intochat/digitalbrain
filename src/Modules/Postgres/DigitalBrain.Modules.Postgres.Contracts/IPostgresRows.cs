using DigitalBrain.Contracts.Data;

namespace DigitalBrain.Postgres;

[Alias("postgres.rows"), Orleans.Metadata.DefaultGrainType("data.postgres-rows")]
public interface IPostgresRows : IRowSource
{
}
