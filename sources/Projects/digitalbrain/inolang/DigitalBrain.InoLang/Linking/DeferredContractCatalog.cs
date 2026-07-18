namespace DigitalBrain.InoLang.Linking;

// v5 C1: the catalog you use when you don't have a catalog. Returns a stub
// schema for every FQN so the Linker can compile a .ino without knowing the
// shape of its references. The runtime resolves shapes at activation time
// and emits Neuron.UnresolvedReference if the FQN never materialises.
//
// Replaces the boot-time MapCatalog.With(...) hand-build. Use this when the
// caller does not need field-level validation — bootstrappers, the LLM's
// Creator path, hot-reload from disk, anything that's already comfortable
// failing soft at activation.
public sealed class DeferredContractCatalog : IContractCatalog
{
    public static readonly DeferredContractCatalog Instance = new();

    public ContractSchema? Resolve(string fqn) =>
        fqn.Contains('.')
            ? new(fqn, ContractKind.Synapse, [], IsDeferred: true)
            : null;

    public IReadOnlyCollection<ContractSchema> GetAllSchemas() => [];

    public void Register(ContractSchema schema) { }
}
