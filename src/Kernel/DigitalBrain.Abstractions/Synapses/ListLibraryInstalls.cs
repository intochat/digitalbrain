namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.list-library-installs")]
public sealed record ListLibraryInstalls(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] ActorContext? Actor = null) : RequestSynapse<LibraryInstallsListed>;

