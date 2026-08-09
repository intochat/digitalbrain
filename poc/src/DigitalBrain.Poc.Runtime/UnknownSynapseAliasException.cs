namespace DigitalBrain.Poc.Runtime;

public sealed class UnknownSynapseAliasException(string alias) : Exception(
    $"No exact handler is registered for synapse alias '{alias}'.");
