namespace DigitalBrain.Runtime.Runtime;

// Wire identity for the E-RUN runtime contracts. Mirrors the
// IntrospectorSynapseTypes precedent: every record on the bus has its FQN
// pinned here so renames are caught by the public-ABI freeze tests and
// signed bundles bind against a single source of truth.
public static class RuntimeSynapseTypes
{
    public const string NeuronDescriptor = "DigitalBrain.Kernel.Contracts.Runtime.NeuronDescriptor";
    public const string IncomingPort     = "DigitalBrain.Kernel.Contracts.Runtime.IncomingPort";
    public const string SynapseEnvelope  = "DigitalBrain.Kernel.Contracts.Runtime.SynapseEnvelope";
    public const string ScenarioGateResult = "DigitalBrain.Kernel.Contracts.Runtime.ScenarioGateResult";
}
