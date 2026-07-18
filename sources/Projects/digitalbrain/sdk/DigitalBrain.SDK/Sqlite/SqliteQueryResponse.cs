using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.Sqlite;

[GenerateSerializer]
public sealed record SqliteQueryResponse([property: Id(1)] IReadOnlyList<string> Columns,
    [property: Id(2)] IReadOnlyList<IReadOnlyList<string?>> Rows
) : Synapse;
