namespace DigitalBrain.AI;

public sealed record CapabilityCandidate(
    string Kind,
    string ContractId,
    int? SchemaVersion,
    string? ModuleId,
    string? NeuronContractId,
    string? BehaviorId,
    string? ArtifactHash,
    string SourceKey);
