namespace DigitalBrain.Poc.Runtime;

public sealed record ExactHandler(
    string ContractAlias,
    Type SynapseType,
    Type NeuronType,
    Type HandlerInterface);
