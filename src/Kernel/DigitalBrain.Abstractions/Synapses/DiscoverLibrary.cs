namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.discover-library")]
public sealed record DiscoverLibrary(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string Intent,
    [property: Id(2)] int Limit = 8) : RequestSynapse<LibraryDiscoveries>;

