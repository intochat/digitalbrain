namespace DigitalBrain.Abstractions.Library;

[GenerateSerializer]
[Alias("db.publish-library-artifact")]
public sealed record PublishLibraryArtifact(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string Name,
    [property: Id(2)] string Version,
    [property: Id(3)] string Description,
    // JSON structure: members [{grainType,localName,role,note?}], optional numbers map for demos.
    [property: Id(4)] string StructureJson,
    [property: Id(5)] ActorContext? Publisher = null) : RequestSynapse<LibraryArtifactPublished>;

