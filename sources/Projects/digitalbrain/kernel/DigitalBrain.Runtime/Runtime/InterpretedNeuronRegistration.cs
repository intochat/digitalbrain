namespace DigitalBrain.Runtime.Runtime;

// E-SDK #63. Carries a production interpreted neuron's descriptor plus the
// signal-subscription FQNs derived from its `on signal(T):` handlers. Held
// separate from NeuronDescriptor (which is a stable Orleans-serialized
// public ABI) so sources can supply the catalog signal-subscription list
// without LinkedPortCatalogContributor needing a LinkedNeuron at startup.
public sealed record InterpretedNeuronRegistration(
    NeuronDescriptor Descriptor,
    IReadOnlyList<string> HandledSignalSubscriptions);
