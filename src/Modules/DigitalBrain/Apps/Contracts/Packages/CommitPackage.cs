using DigitalBrain.Coding;

namespace DigitalBrain.Apps;

// MergeFrom makes a merge commit: the new content reconciles the head with that revision.
[GenerateSerializer, Alias("apps.commit-package")]
public sealed record CommitPackage(
    [property: Id(0)] Guid OperationId,
    [property: Id(1)] string? ExpectedHead,
    [property: Id(2)] PackageContent Content,
    [property: Id(3)] CodeArtifactRef Artifact,
    [property: Id(4)] string Message,
    [property: Id(5)] PackageRevisionRef? MergeFrom = null);
