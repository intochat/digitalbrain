namespace DigitalBrain.Apps;

// Id hashes the parents and content, so forks share ancestry and identical work has one identity.
[GenerateSerializer, Alias("apps.package-revision")]
public sealed record PackageRevision(
    [property: Id(0)] string Id,
    [property: Id(1)] string[] Parents,
    [property: Id(2)] PackageContent Content,
    [property: Id(4)] string Author,
    [property: Id(5)] string Message,
    [property: Id(6)] DateTimeOffset CommittedAt);
