namespace DigitalBrain.Postgres;

[GenerateSerializer]
[Alias("db.postgres.read-schema")]
public sealed record ReadPostgresSchema([property: Id(0)] string? Table = null);