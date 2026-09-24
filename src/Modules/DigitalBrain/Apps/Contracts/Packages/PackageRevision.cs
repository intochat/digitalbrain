using DigitalBrain.Coding;

namespace DigitalBrain.Apps;

// Id hashes the parents and content, so forks share ancestry and identical work has one identity.
// Artifact is the passing check that vouches for exactly this source and these tests.
[GenerateSerializer, Alias("apps.package-revision")]
public sealed record PackageRevision(
    [property: Id(0)] string Id,
    [property: Id(1)] IReadOnlyList<string> Parents,
    [property: Id(2)] PackageContent Content,
    [property: Id(3)] CodeArtifactRef Artifact,
    [property: Id(4)] string Author,
    [property: Id(5)] string Message,
    [property: Id(6)] DateTimeOffset CommittedAt);
