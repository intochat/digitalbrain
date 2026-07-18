namespace DigitalBrain.InoLang.Linking;

// v5 C2: Signal collapses into Synapse — there is exactly one wire concept,
// distinguished from a callable Neuron target. Routing (broadcast vs P2P)
// is a property of how a Synapse is sent, not a property of its schema.
public enum ContractKind { Synapse, Neuron }

// IsDeferred = true marks a stub schema synthesized by DeferredContractCatalog
// for v5 lazy-resolution mode. The Linker skips field-shape validation on
// deferred schemas; the runtime resolves the real shape at activation.
public sealed record ContractSchema(
    string Fqn,
    ContractKind Kind,
    IReadOnlyList<string> Fields,
    bool IsDeferred = false);
