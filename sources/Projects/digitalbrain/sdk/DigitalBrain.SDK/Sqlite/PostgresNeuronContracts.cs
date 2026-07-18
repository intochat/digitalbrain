using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.Sqlite;

[GenerateSerializer]
public sealed record RunMigrationsRequest : Synapse;

[GenerateSerializer]
public sealed record MigrationsApplied : Synapse;

[GenerateSerializer]
public sealed record MigrationsFailed(
    [property: Id(1)] string ErrorMessage = ""
) : Synapse;

[GenerateSerializer]
public sealed record PgPingRequest : Synapse;

[GenerateSerializer]
public sealed record PgPong : Synapse;

[GenerateSerializer]
public sealed record PgUnavailable(
    [property: Id(1)] string ErrorMessage = ""
) : Synapse;

