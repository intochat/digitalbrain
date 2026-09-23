using DigitalBrain.Apps;
using DigitalBrain.Broker;

namespace DigitalBrain.Marketplace;

public enum ScanSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
}

[GenerateSerializer, Alias("marketplace.scan-finding")]
public sealed record ScanFinding
{
    [Id(0)] public required string Scanner { get; init; }
    [Id(1)] public required ScanSeverity Severity { get; init; }
    [Id(2)] public required string Message { get; init; }
}

public interface IManifestScanner
{
    IReadOnlyList<ScanFinding> Scan(AppManifest manifest, byte[]? artifact);
}

[GenerateSerializer, Alias("marketplace.scenario-result")]
public sealed record ScenarioResult
{
    [Id(0)] public required string Name { get; init; }
    [Id(1)] public required bool Passed { get; init; }
    [Id(2)] public string? Detail { get; init; }
}

// Runs the manifest's declared scenarios against deterministic fakes in the sandbox.
public interface IManifestScenarioRunner
{
    Task<IReadOnlyList<ScenarioResult>> RunAsync(AppManifest manifest, CancellationToken cancellationToken = default);
}

[GenerateSerializer, Alias("marketplace.golden-prompt-report")]
public sealed record GoldenPromptReport
{
    [Id(0)] public required int Total { get; init; }
    [Id(1)] public required int Accepted { get; init; }

    public double Precision => Total == 0 ? 1d : (double)Accepted / Total;
}

public interface IGoldenPromptEvaluator
{
    Task<GoldenPromptReport> EvaluateAsync(AppManifest manifest, CancellationToken cancellationToken = default);
}

public enum CertificationOutcome
{
    Certified = 0,
    PendingHumanReview = 1,
    Rejected = 2,
}

// The marketplace's own copy of the declared-versus-used diff, so certification evidence can be
// persisted without depending on the broker's non-serialized result type.
[GenerateSerializer, Alias("marketplace.declared-versus-used-diff")]
public sealed record DeclaredVersusUsedDiff
{
    [Id(0)] public required string AppId { get; init; }
    [Id(1)] public IReadOnlyList<string> UndeclaredDataClasses { get; init; } = [];
    [Id(2)] public IReadOnlyList<string> UnusedPermissions { get; init; } = [];
    [Id(3)] public IReadOnlyList<string> UndeclaredMeters { get; init; } = [];

    public bool IsClean => UndeclaredDataClasses.Count == 0 && UndeclaredMeters.Count == 0;

    public static DeclaredVersusUsedDiff From(DeclaredObservedDiff diff)
    {
        ArgumentNullException.ThrowIfNull(diff);
        return new DeclaredVersusUsedDiff
        {
            AppId = diff.AppId,
            UndeclaredDataClasses = diff.UndeclaredDataClasses,
            UnusedPermissions = diff.UnusedPermissions,
            UndeclaredMeters = diff.UndeclaredMeters,
        };
    }
}

// Every listing carries its certification evidence. A pass means every automated gate passed; a
// risky data class additionally queues a human review before the listing can be published.
[GenerateSerializer, Alias("marketplace.certification")]
public sealed record CertificationEvidence
{
    [Id(0)] public required string AppId { get; init; }
    [Id(1)] public required string Version { get; init; }
    [Id(2)] public required CertificationOutcome Outcome { get; init; }
    [Id(3)] public required NamespaceProofEvidence NamespaceProof { get; init; }
    [Id(4)] public required SignatureOutcome Signature { get; init; }
    [Id(5)] public IReadOnlyList<ScanFinding> Findings { get; init; } = [];
    [Id(6)] public IReadOnlyList<ScenarioResult> Scenarios { get; init; } = [];
    [Id(7)] public required DeclaredVersusUsedDiff Diff { get; init; }
    [Id(8)] public required GoldenPromptReport GoldenPrompts { get; init; }
    [Id(9)] public IReadOnlyList<string> RiskyDataClasses { get; init; } = [];
    [Id(10)] public IReadOnlyList<string> Failures { get; init; } = [];
    [Id(11)] public required DateTimeOffset CertifiedAt { get; init; }

    public bool Passed => Outcome == CertificationOutcome.Certified;
}

public sealed record CertificationRequest
{
    public required AppManifest Manifest { get; init; }
    public required PublisherProfile Publisher { get; init; }
    public AppSignature? Signature { get; init; }
    public byte[]? Artifact { get; init; }
}

public interface ICertificationService
{
    Task<CertificationEvidence> CertifyAsync(CertificationRequest request, CancellationToken cancellationToken = default);
}

// The data classes that force a human review rather than a fully automated certification.
public static class RiskyDataClasses
{
    public static readonly IReadOnlySet<string> Types = new HashSet<string>(StringComparer.Ordinal)
    {
        "person.birthDate",
        "person.email",
        "person.phone",
        "credential",
        "secret-ref",
        "bank-account",
    };
}
