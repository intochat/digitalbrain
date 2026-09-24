namespace DigitalBrain.Apps;

[GenerateSerializer, Alias("apps.app-snapshot")]
public sealed record AppSnapshot(
    [property: Id(0)] AppStatus Status,
    [property: Id(1)] PackageRevisionRef? Revision,
    [property: Id(2)] IReadOnlyDictionary<string, string> Settings,
    [property: Id(3)] IReadOnlyList<PackageOperation> Operations,
    [property: Id(4)] string? BehaviorProgram);
