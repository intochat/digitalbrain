namespace DigitalBrain.InoLang.Linking;

// Plan 1: an injected abstraction (FakeCatalog in tests). Plan 2 (E-RUN) supplies
// the real cluster-catalog-backed, reflection-over-.Contracts implementation.
public interface IContractCatalog
{
    ContractSchema? Resolve(string fqn);
    IReadOnlyCollection<ContractSchema> GetAllSchemas();
    void Register(ContractSchema schema);
}
