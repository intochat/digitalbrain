using System.Text.Json.Serialization;

namespace DigitalBrain.Catalog;

public static class CatalogContractLimits
{
    public const int DiscoveryEvidenceItems = 32;
    public const int DiscoveryEvidenceCodeLength = 64;
    public const int DiscoveryResults = 50;
    public const int ReasonLength = 256;
}

[GenerateSerializer]
[Alias("db.catalog.availability-status")]
public enum CatalogAvailabilityStatus
{
    Unknown = 0,
    Available = 1,
    Degraded = 2,
    Unavailable = 3,
}

[GenerateSerializer]
[Alias("db.catalog.availability-requirement")]
public enum CatalogAvailabilityRequirement
{
    Any = 0,
    CurrentlyAvailable = 1,
}

[GenerateSerializer]
[Alias("db.catalog.availability-snapshot")]
public sealed record CatalogAvailabilitySnapshot
{
    [JsonConstructor]
    public CatalogAvailabilitySnapshot(
        CatalogAvailabilityStatus status,
        DateTimeOffset observedAt,
        string? reason)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        Status = status;
        ObservedAt = observedAt;
        Reason = CatalogContractValidation.OptionalBounded(
            reason,
            nameof(reason),
            CatalogContractLimits.ReasonLength);
    }

    [Id(0)] public CatalogAvailabilityStatus Status { get; }
    [Id(1)] public DateTimeOffset ObservedAt { get; }
    [Id(2)] public string? Reason { get; }

    public static CatalogAvailabilitySnapshot Unknown(DateTimeOffset observedAt)
        => new(CatalogAvailabilityStatus.Unknown, observedAt, null);
}

[GenerateSerializer]
[Alias("db.catalog.discovery-compatibility")]
public sealed record DiscoveryCompatibility
{
    [JsonConstructor]
    public DiscoveryCompatibility(
        string? OperationId,
        string? OperationVersion,
        string? CapabilityId,
        string? CapabilityVersion,
        string? SignalAlias,
        string? SignalSchemaHash,
        string? InputSchemaId,
        string? InputSchemaHash,
        string? OutputSchemaId,
        string? OutputSchemaHash,
        CatalogLifecycle? Lifecycle,
        CatalogConfigurationState? ConfigurationState,
        bool RequireInvocable)
    {
        if (Lifecycle is { } lifecycle && !Enum.IsDefined(lifecycle))
        {
            throw new ArgumentOutOfRangeException(nameof(Lifecycle));
        }

        if (ConfigurationState is { } configurationState && !Enum.IsDefined(configurationState))
        {
            throw new ArgumentOutOfRangeException(nameof(ConfigurationState));
        }

        this.OperationId = CatalogContractValidation.Optional(OperationId);
        this.OperationVersion = CatalogContractValidation.Optional(OperationVersion);
        this.CapabilityId = CatalogContractValidation.Optional(CapabilityId);
        this.CapabilityVersion = CatalogContractValidation.Optional(CapabilityVersion);
        this.SignalAlias = CatalogContractValidation.Optional(SignalAlias);
        this.SignalSchemaHash = ValidateOptionalHash(SignalSchemaHash, nameof(SignalSchemaHash));
        this.InputSchemaId = CatalogContractValidation.Optional(InputSchemaId);
        this.InputSchemaHash = ValidateOptionalHash(InputSchemaHash, nameof(InputSchemaHash));
        this.OutputSchemaId = CatalogContractValidation.Optional(OutputSchemaId);
        this.OutputSchemaHash = ValidateOptionalHash(OutputSchemaHash, nameof(OutputSchemaHash));
        this.Lifecycle = Lifecycle;
        this.ConfigurationState = ConfigurationState;
        this.RequireInvocable = RequireInvocable;
    }

    [Id(0)] public string? OperationId { get; }
    [Id(1)] public string? OperationVersion { get; }
    [Id(2)] public string? CapabilityId { get; }
    [Id(3)] public string? CapabilityVersion { get; }
    [Id(4)] public string? SignalAlias { get; }
    [Id(5)] public string? SignalSchemaHash { get; }
    [Id(6)] public string? InputSchemaId { get; }
    [Id(7)] public string? InputSchemaHash { get; }
    [Id(8)] public string? OutputSchemaId { get; }
    [Id(9)] public string? OutputSchemaHash { get; }
    [Id(10)] public CatalogLifecycle? Lifecycle { get; }
    [Id(11)] public CatalogConfigurationState? ConfigurationState { get; }
    [Id(12)] public bool RequireInvocable { get; }

    public DiscoveryCompatibility Normalize()
        => new(
            OperationId,
            OperationVersion,
            CapabilityId,
            CapabilityVersion,
            SignalAlias,
            SignalSchemaHash,
            InputSchemaId,
            InputSchemaHash,
            OutputSchemaId,
            OutputSchemaHash,
            Lifecycle,
            ConfigurationState,
            RequireInvocable);

    private static string? ValidateOptionalHash(string? value, string parameterName)
    {
        var normalized = CatalogContractValidation.Optional(value);
        if (normalized is not null)
        {
            try
            {
                _ = new CatalogFingerprint(normalized);
            }
            catch (ArgumentException exception)
            {
                throw new ArgumentException("A schema hash must be a lowercase SHA-256 value.", parameterName, exception);
            }
        }

        return normalized;
    }
}

[GenerateSerializer]
[Alias("db.catalog.discovery-query")]
public sealed record DiscoveryQuery
{
    [JsonConstructor]
    public DiscoveryQuery(
        string Text,
        IReadOnlyList<CatalogEntryKind>? Kinds,
        IReadOnlyList<string>? RequiredTags,
        DiscoveryCompatibility? Compatibility,
        CatalogAvailabilityRequirement Availability,
        int Limit,
        string? Cursor)
    {
        if (!Enum.IsDefined(Availability))
        {
            throw new ArgumentOutOfRangeException(nameof(Availability));
        }

        if (Limit is < 1 or > 50)
        {
            throw new ArgumentOutOfRangeException(nameof(Limit), "Discovery limit must be from 1 through 50.");
        }

        if (Kinds?.Any(static kind => !Enum.IsDefined(kind)) is true)
        {
            throw new ArgumentOutOfRangeException(nameof(Kinds));
        }

        this.Text = Text?.Trim() ?? string.Empty;
        this.Kinds = Kinds is null
            ? null
            : CatalogContractValidation.ReadOnlyCopy(Kinds.Distinct().Order().ToArray());
        this.RequiredTags = CatalogContractValidation.Set(RequiredTags, nameof(RequiredTags));
        this.Compatibility = Compatibility?.Normalize();
        this.Availability = Availability;
        this.Limit = Limit;
        this.Cursor = CatalogContractValidation.OpaqueOptional(Cursor, nameof(Cursor));
    }

    [Id(0)] public string Text { get; }
    [Id(1)] public IReadOnlyList<CatalogEntryKind>? Kinds { get; }
    [Id(2)] public IReadOnlyList<string>? RequiredTags { get; }
    [Id(3)] public DiscoveryCompatibility? Compatibility { get; }
    [Id(4)] public CatalogAvailabilityRequirement Availability { get; }
    [Id(5)] public int Limit { get; }
    [Id(6)] public string? Cursor { get; }

    public DiscoveryQuery Normalize()
        => new(Text, Kinds, RequiredTags, Compatibility, Availability, Limit, Cursor);
}

[GenerateSerializer]
[Alias("db.catalog.discovery-exact-match-kind")]
public enum DiscoveryExactMatchKind
{
    None = 0,
    NameOrAlias = 1,
    OperationOrCapabilityId = 2,
    DescriptorId = 3,
}

[Flags]
[GenerateSerializer]
[Alias("db.catalog.compatibility-evidence")]
public enum DiscoveryCompatibilityEvidence
{
    None = 0,
    Kind = 1 << 0,
    RequiredTag = 1 << 1,
    OperationOrCapability = 1 << 2,
    Signal = 1 << 3,
    InputSchema = 1 << 4,
    OutputSchema = 1 << 5,
    Lifecycle = 1 << 6,
    Invocability = 1 << 7,
    Configuration = 1 << 8,
}

[GenerateSerializer]
[Alias("db.catalog.discovery-evidence")]
public sealed record DiscoveryEvidence
{
    [JsonConstructor]
    public DiscoveryEvidence(
        DiscoveryExactMatchKind ExactMatch,
        DiscoveryCompatibilityEvidence Compatibility,
        int? ExactRank,
        int? LexicalRank,
        int? SemanticRank,
        float? SemanticSimilarity,
        IReadOnlyList<string>? MatchedFields,
        IReadOnlyList<string>? RankReasons)
    {
        if (!Enum.IsDefined(ExactMatch))
        {
            throw new ArgumentOutOfRangeException(nameof(ExactMatch));
        }

        const DiscoveryCompatibilityEvidence allCompatibilityEvidence =
            DiscoveryCompatibilityEvidence.Kind |
            DiscoveryCompatibilityEvidence.RequiredTag |
            DiscoveryCompatibilityEvidence.OperationOrCapability |
            DiscoveryCompatibilityEvidence.Signal |
            DiscoveryCompatibilityEvidence.InputSchema |
            DiscoveryCompatibilityEvidence.OutputSchema |
            DiscoveryCompatibilityEvidence.Lifecycle |
            DiscoveryCompatibilityEvidence.Invocability |
            DiscoveryCompatibilityEvidence.Configuration;
        if ((Compatibility & ~allCompatibilityEvidence) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Compatibility));
        }

        ValidateRank(ExactRank, nameof(ExactRank));
        ValidateRank(LexicalRank, nameof(LexicalRank));
        ValidateRank(SemanticRank, nameof(SemanticRank));
        if (ExactMatch == DiscoveryExactMatchKind.None != (ExactRank is null))
        {
            throw new ArgumentException("An exact rank is present if and only if an exact match is present.");
        }

        if (SemanticSimilarity is { } similarity && !float.IsFinite(similarity))
        {
            throw new ArgumentOutOfRangeException(nameof(SemanticSimilarity));
        }

        this.ExactMatch = ExactMatch;
        this.Compatibility = Compatibility;
        this.ExactRank = ExactRank;
        this.LexicalRank = LexicalRank;
        this.SemanticRank = SemanticRank;
        this.SemanticSimilarity = SemanticSimilarity;
        this.MatchedFields = CatalogContractValidation.BoundedSet(
            MatchedFields,
            nameof(MatchedFields),
            CatalogContractLimits.DiscoveryEvidenceItems,
            CatalogContractLimits.DiscoveryEvidenceCodeLength);
        this.RankReasons = CatalogContractValidation.BoundedSet(
            RankReasons,
            nameof(RankReasons),
            CatalogContractLimits.DiscoveryEvidenceItems,
            CatalogContractLimits.DiscoveryEvidenceCodeLength);
    }

    [Id(0)] public DiscoveryExactMatchKind ExactMatch { get; }
    [Id(1)] public DiscoveryCompatibilityEvidence Compatibility { get; }
    [Id(2)] public int? LexicalRank { get; }
    [Id(3)] public int? SemanticRank { get; }
    [Id(4)] public float? SemanticSimilarity { get; }
    [Id(5)] public IReadOnlyList<string> MatchedFields { get; }
    [Id(6)] public IReadOnlyList<string> RankReasons { get; }
    [Id(7)] public int? ExactRank { get; }

    private static void ValidateRank(int? rank, string parameterName)
    {
        if (rank is <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Lane ranks must be positive and one-based.");
        }
    }
}

[GenerateSerializer]
[Alias("db.catalog.discovery-candidate")]
public sealed record DiscoveryCandidate
{
    [JsonConstructor]
    public DiscoveryCandidate(
        CatalogReference reference,
        CatalogEntryKind kind,
        string name,
        string summary,
        CatalogTypedReference target,
        CatalogLifecycle lifecycle,
        CatalogConfigurationState configurationState,
        CatalogAvailabilitySnapshot availability,
        int finalRank,
        double rrfScore,
        DiscoveryEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(availability);
        ArgumentNullException.ThrowIfNull(evidence);
        if (!Enum.IsDefined(kind) || !Enum.IsDefined(lifecycle) || !Enum.IsDefined(configurationState))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), "A discovery candidate enum value is invalid.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(finalRank);
        if (!double.IsFinite(rrfScore) || rrfScore < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rrfScore));
        }

        reference.Validate();
        target.ValidateFor(kind, reference.Scope);
        Reference = reference;
        Kind = kind;
        Name = CatalogContractValidation.Required(name, nameof(name));
        Summary = CatalogContractValidation.Required(summary, nameof(summary));
        Target = target;
        Lifecycle = lifecycle;
        ConfigurationState = configurationState;
        Availability = availability;
        FinalRank = finalRank;
        RrfScore = rrfScore;
        Evidence = evidence;
    }

    [Id(0)] public CatalogReference Reference { get; }
    [Id(1)] public CatalogEntryKind Kind { get; }
    [Id(2)] public string Name { get; }
    [Id(3)] public string Summary { get; }
    [Id(4)] public CatalogTypedReference Target { get; }
    [Id(5)] public CatalogLifecycle Lifecycle { get; }
    [Id(6)] public CatalogConfigurationState ConfigurationState { get; }
    [Id(7)] public CatalogAvailabilitySnapshot Availability { get; }
    [Id(8)] public int FinalRank { get; }
    [Id(9)] public double RrfScore { get; }
    [Id(10)] public DiscoveryEvidence Evidence { get; }
}

[GenerateSerializer]
[Alias("db.catalog.discovery-status")]
public enum DiscoveryStatus
{
    Ready = 0,
    SemanticDegraded = 1,
    Initializing = 2,
    StaleCursor = 3,
}

[GenerateSerializer]
[Alias("db.catalog.discovery-diagnostics")]
public sealed record DiscoveryDiagnostics
{
    [JsonConstructor]
    public DiscoveryDiagnostics(
        long metadataWatermark,
        string metadataSnapshotFingerprint,
        long availabilityWatermark,
        string availabilitySnapshotToken,
        string? semanticGenerationId,
        string? semanticSnapshotToken,
        bool candidatePoolTruncated,
        string? degradationReason)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(metadataWatermark);
        ArgumentOutOfRangeException.ThrowIfNegative(availabilityWatermark);
        _ = new CatalogFingerprint(metadataSnapshotFingerprint);
        if (semanticSnapshotToken is not null && semanticGenerationId is null)
        {
            throw new ArgumentException("A semantic snapshot token requires a semantic generation.");
        }

        MetadataWatermark = metadataWatermark;
        MetadataSnapshotFingerprint = metadataSnapshotFingerprint;
        AvailabilityWatermark = availabilityWatermark;
        AvailabilitySnapshotToken = CatalogContractValidation.OpaqueRequired(
            availabilitySnapshotToken,
            nameof(availabilitySnapshotToken));
        SemanticGenerationId = CatalogContractValidation.OpaqueOptional(
            semanticGenerationId,
            nameof(semanticGenerationId));
        SemanticSnapshotToken = CatalogContractValidation.OpaqueOptional(
            semanticSnapshotToken,
            nameof(semanticSnapshotToken));
        CandidatePoolTruncated = candidatePoolTruncated;
        DegradationReason = CatalogContractValidation.OptionalBounded(
            degradationReason,
            nameof(degradationReason),
            CatalogContractLimits.ReasonLength);
    }

    [Id(0)] public long MetadataWatermark { get; }
    [Id(1)] public string MetadataSnapshotFingerprint { get; }
    [Id(2)] public long AvailabilityWatermark { get; }
    [Id(3)] public string AvailabilitySnapshotToken { get; }
    [Id(4)] public string? SemanticGenerationId { get; }
    [Id(5)] public string? SemanticSnapshotToken { get; }
    [Id(6)] public bool CandidatePoolTruncated { get; }
    [Id(7)] public string? DegradationReason { get; }
}

[GenerateSerializer]
[Alias("db.catalog.discovery-result")]
public sealed record DiscoveryResult
{
    [JsonConstructor]
    public DiscoveryResult(
        DiscoveryStatus Status,
        IReadOnlyList<DiscoveryCandidate>? Candidates,
        DiscoveryDiagnostics Diagnostics,
        string? NextCursor)
    {
        ArgumentNullException.ThrowIfNull(Diagnostics);
        if (!Enum.IsDefined(Status))
        {
            throw new ArgumentOutOfRangeException(nameof(Status));
        }

        var cursor = CatalogContractValidation.OpaqueOptional(NextCursor, nameof(NextCursor));
        if (cursor is not null && Status != DiscoveryStatus.Ready)
        {
            throw new ArgumentException("Only a ready discovery result can carry a next cursor.", nameof(NextCursor));
        }

        if (cursor is not null &&
            (Diagnostics.SemanticGenerationId is null || Diagnostics.SemanticSnapshotToken is null))
        {
            throw new ArgumentException(
                "A next cursor requires a complete semantic snapshot identity.",
                nameof(NextCursor));
        }

        var copiedCandidates = CatalogContractValidation.ReadOnlyCopy(Candidates);
        if (copiedCandidates.Count > CatalogContractLimits.DiscoveryResults)
        {
            throw new ArgumentException(
                $"A discovery result cannot contain more than {CatalogContractLimits.DiscoveryResults} candidates.",
                nameof(Candidates));
        }

        if (copiedCandidates.Any(static candidate => candidate is null))
        {
            throw new ArgumentException("Discovery candidates cannot contain null values.", nameof(Candidates));
        }

        if (Status is DiscoveryStatus.Initializing or DiscoveryStatus.StaleCursor && copiedCandidates.Count != 0)
        {
            throw new ArgumentException(
                "Initializing and stale-cursor results cannot carry candidates.",
                nameof(Candidates));
        }

        this.Status = Status;
        this.Candidates = copiedCandidates;
        this.Diagnostics = Diagnostics;
        this.NextCursor = cursor;
    }

    [Id(0)] public DiscoveryStatus Status { get; }
    [Id(1)] public IReadOnlyList<DiscoveryCandidate> Candidates { get; }
    [Id(2)] public DiscoveryDiagnostics Diagnostics { get; }
    [Id(3)] public string? NextCursor { get; }
}
