namespace DigitalBrain.Apps;

[GenerateSerializer, Alias("apps.spec-token")]
public sealed record SpecToken(
    [property: Id(0)] string Name,
    [property: Id(1)] string Kind,
    [property: Id(2)] string QualifiedName,
    [property: Id(3)] string Module,
    [property: Id(4)] string? Description);
