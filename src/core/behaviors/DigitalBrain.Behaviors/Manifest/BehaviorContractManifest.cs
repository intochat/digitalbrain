namespace DigitalBrain.Behaviors.Manifest;

public sealed record BehaviorContractCaseManifest(
    string CaseId,
    int CaseSchemaVersion,
    string CaseName,
    string PayloadSchemaJson);

public sealed record BehaviorContractManifest(
    string BehaviorContractId,
    int ContractMajorVersion,
    string OneOfSchemaJson,
    IReadOnlyList<BehaviorContractCaseManifest> Cases,
    string ResultSchemaJson);
