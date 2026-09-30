namespace DigitalBrain.Postgres;

[GenerateSerializer]
[Alias("db.postgres.connection")]
public sealed record PostgresConnection(
    [property: Id(0)] bool Connected,
    [property: Id(1)] string Database,
    [property: Id(2)] string? ServerVersion,
    [property: Id(3)] string Provider);
