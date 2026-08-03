namespace DigitalBrain.Behaviors.Manifest;

public sealed record BehaviorCapabilityGrant(
    string TargetNeuronContractId,
    string AcceptedRequestSynapseId,
    int AcceptedRequestSchemaVersion,
    string? EmittedResultSynapseId,
    int? EmittedResultSchemaVersion,
    string TargetInstancePolicy,
    string TargetInstanceName);
