namespace DigitalBrain.Apps;

[GenerateSerializer, Alias("apps.package-listing")]
public sealed record PackageListing(
    [property: Id(0)] PackageId Package,
    [property: Id(1)] string Title,
    [property: Id(2)] string Description,
    [property: Id(3)] string Revision,
    [property: Id(4)] PackageRevisionRef? ForkedFrom,
    [property: Id(5)] DateTimeOffset PublishedAt);
