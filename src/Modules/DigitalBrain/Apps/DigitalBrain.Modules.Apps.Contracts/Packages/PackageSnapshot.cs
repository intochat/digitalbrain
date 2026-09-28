namespace DigitalBrain.Apps;

[GenerateSerializer, Alias("apps.package-snapshot")]
public sealed record PackageSnapshot(
    [property: Id(0)] PackageId Id,
    [property: Id(1)] PackageRevisionRef? ForkedFrom,
    [property: Id(2)] string? Head,
    [property: Id(3)] string? Published,
    [property: Id(4)] IReadOnlyList<PackageRevisionSummary> History,
    [property: Id(5)] IReadOnlyList<PackageProposal> Proposals);
