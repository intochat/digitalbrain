namespace DigitalBrain.Core;

public sealed record CapabilityHit(
    string Kind,
    string ContractId,
    string Signature,
    string? NeuronContractId,
    string? DefaultInstanceName,
    double Score)
{
    public const string RequestKind = "request";
    public const string FactKind = "fact";
}