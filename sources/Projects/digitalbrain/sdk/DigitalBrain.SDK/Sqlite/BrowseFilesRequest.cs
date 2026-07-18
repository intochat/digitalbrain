using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.Sqlite;

[GenerateSerializer]
public sealed record BrowseFilesRequest([property: Id(1)] string GlobPattern,
    [property: Id(2)] int MaxCount
) : Synapse;
