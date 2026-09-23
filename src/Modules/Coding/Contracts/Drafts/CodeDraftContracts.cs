namespace DigitalBrain.Coding;

[GenerateSerializer, Alias("coding.save-draft")]
public sealed record SaveCodeDraft(
    [property: Id(0)] long ExpectedRevision,
    [property: Id(1)] Guid OperationId,
    [property: Id(2)] string Source,
    [property: Id(3)] string Tests,
    [property: Id(4)] IReadOnlyList<string> ModuleIds);

[GenerateSerializer, Alias("coding.check-draft")]
public sealed record CheckCodeDraft([property: Id(0)] long Revision, [property: Id(1)] Guid OperationId);

[GenerateSerializer, Alias("coding.draft-snapshot")]
public sealed record CodeDraftSnapshot(
    [property: Id(0)] long Revision,
    [property: Id(1)] string Source,
    [property: Id(2)] string Tests,
    [property: Id(3)] IReadOnlyList<string> ModuleIds,
    [property: Id(4)] Guid? LatestCheckId);

[Alias("coding.check-status")]
public enum CodeCheckStatus { Queued, Building, Testing, Passed, Failed, Cancelled, Interrupted }

[GenerateSerializer, Alias("coding.check-diagnostic")]
public sealed record CodeCheckDiagnostic(
    [property: Id(0)] string Code,
    [property: Id(1)] string Severity,
    [property: Id(2)] string Message,
    [property: Id(3)] string? File = null,
    [property: Id(4)] int? Line = null,
    [property: Id(5)] int? Column = null);

[GenerateSerializer, Alias("coding.test-counts")]
public sealed record CodeTestCounts(
    [property: Id(0)] int Discovered,
    [property: Id(1)] int Passed,
    [property: Id(2)] int Failed,
    [property: Id(3)] int Skipped);

[GenerateSerializer, Alias("coding.check-snapshot")]
public sealed record CodeCheckSnapshot(
    [property: Id(0)] Guid OperationId,
    [property: Id(1)] long Revision,
    [property: Id(2)] CodeCheckStatus Status,
    [property: Id(3)] IReadOnlyList<CodeCheckDiagnostic> Diagnostics,
    [property: Id(4)] CodeTestCounts? Tests,
    [property: Id(5)] DateTimeOffset CreatedAt,
    [property: Id(6)] DateTimeOffset? CompletedAt,
    [property: Id(7)] CodeArtifactRef? Artifact);