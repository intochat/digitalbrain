using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.Sqlite;

[GenerateSerializer]
public sealed record BrowseFilesResponse([property: Id(1)] IReadOnlyList<string> Paths
) : Synapse;
