namespace DigitalBrain.Apps;

[GenerateSerializer, Alias("apps.package-revision-summary")]
public sealed record PackageRevisionSummary(
    [property: Id(0)] string Id,
    [property: Id(1)] IReadOnlyList<string> Parents,
    [property: Id(2)] string Author,
    [property: Id(3)] string Message,
    [property: Id(4)] DateTimeOffset CommittedAt);
