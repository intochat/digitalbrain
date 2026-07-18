using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.Sqlite;

[GenerateSerializer]
public sealed record SqliteExecResponse([property: Id(1)] int RowsAffected,
    [property: Id(2)] long LastInsertRowId
) : Synapse;
