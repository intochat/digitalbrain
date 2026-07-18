using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.Sqlite;

[GenerateSerializer]
public sealed record ReadFileResponse([property: Id(1)] string FilePath,
    [property: Id(2)] long SizeBytes,
    [property: Id(3)] string Sha256,
    [property: Id(4)] string? ContentBase64,
    [property: Id(5)] string? Error
) : Synapse;
