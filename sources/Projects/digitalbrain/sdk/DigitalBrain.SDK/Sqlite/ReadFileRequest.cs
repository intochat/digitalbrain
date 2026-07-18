using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.Sqlite;

[GenerateSerializer]
public sealed record ReadFileRequest([property: Id(1)] string FilePath,
    [property: Id(2)] bool IncludeContent
) : Synapse;
