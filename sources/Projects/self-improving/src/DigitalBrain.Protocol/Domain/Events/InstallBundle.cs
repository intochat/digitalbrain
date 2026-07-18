using DigitalBrain.Protocol.Domain.ValueObjects.Distribution;
using DigitalBrain.Protocol.Domain.ValueObjects.Identity;

namespace DigitalBrain.Protocol.Domain.Events;

[GenerateSerializer]
public sealed record InstallBundle(
    BundleId BundleId,
    string? SourcePathOrUri = null,
    string? TargetDomainId = null,
    bool IsContractOnly = false,
    ContractDeclaration[]? ContractHandlers = null,
    bool HasRules = false
) : Synapse;
