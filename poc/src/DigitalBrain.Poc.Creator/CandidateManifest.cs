using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DigitalBrain.Poc.Creator;

public sealed record CandidateManifest
{
    public required int SchemaVersion { get; init; }

    public required string Id { get; init; }

    public required string RunId { get; init; }

    public required string FamilyId { get; init; }

    public required CandidateStatus Status { get; init; }

    public required string Source { get; init; }

    public required string Assembly { get; init; }

    public required string SourceHash { get; init; }

    public required string NormalizedAstHash { get; init; }

    public required string FixedHeaderHash { get; init; }

    public required string CompilerHash { get; init; }

    public required string SdkHash { get; init; }

    public required string ReferencesHash { get; init; }

    public required IReadOnlyList<string> ResolvedReferences { get; init; }

    public required string CapabilitiesHash { get; init; }

    public required string ContractsHash { get; init; }

    public required string StateSchemaHash { get; init; }

    public required string AssemblyHash { get; init; }

    public required bool SourceHashVerified { get; init; }

    public required bool AssemblyHashVerified { get; init; }

    [JsonIgnore]
    public string CandidateMetadataHash { get; init; } = string.Empty;
}
