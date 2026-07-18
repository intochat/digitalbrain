using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.Sqlite;

[GenerateSerializer]
public sealed record SqliteExecRequest([property: Id(1)] string DatabaseId,
    [property: Id(2)] string Sql,
    [property: Id(3)] IReadOnlyList<SqliteParameterValue>? Parameters
) : Synapse;
