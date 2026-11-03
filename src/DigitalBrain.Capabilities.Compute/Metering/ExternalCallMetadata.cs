namespace DigitalBrain.Capabilities.Compute;

public sealed record ExternalCallMetadata(string Operation, Funding Funding, CostSubject? Subject, string InvocationId);
