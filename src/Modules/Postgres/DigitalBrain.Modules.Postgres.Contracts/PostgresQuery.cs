namespace DigitalBrain.Postgres;

[GenerateSerializer]
[Alias("db.postgres.query")]
public sealed record PostgresQuery(
    [property: Id(0)] string Sql,
    [property: Id(1)] int MaxRows = PostgresQuery.DefaultMaxRows)
{
    public const int DefaultMaxRows = 200;
    public const int MaxRowsLimit = 1000;
}