namespace DigitalBrain.Protocol.Domain.ValueObjects.Distribution;

[GenerateSerializer]
public sealed record ContractDeclaration(string NeuronInterface, string SynapseType, bool IsHandle);

[GenerateSerializer]
public sealed record ExperienceManifest(
    string Id,
    string Name,
    string Version,
    string Description,
    string Author,
    DateTimeOffset CreatedAt,
    string ContentHash,
    string[] ObservedSynapses,
    string[] Files,
    bool IsContractOnly = false,
    ContractDeclaration[]? ContractHandlers = null,
    bool HasRules = false,
    string? AuthorPublicKeyBase64 = null,
    string? SignatureBase64 = null,
    string? DefaultRegion = null,
    bool DefaultPinned = false,
    int DefaultOrder = 0,
    string[] Requires = null,
    bool IsSystem = false,
    string[] RequiresGrant = null);

[GenerateSerializer]
public sealed record ExperienceListing(
    ExperienceManifest Manifest,
    long SizeBytes,
    DateTimeOffset PublishedAt);

public static class ExperiencePackageFormat
{
    public const string Extension = ".brain";
    public const string ManifestEntry = "manifest.json";
    public const string InoEntry = "experience.ino";
    public const string YamlEntry = "experience.yaml";
    public const string ContractEntry = "contract.json";
}
