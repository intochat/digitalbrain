namespace DigitalBrain.Abstractions.Bundles;

public enum ContractKind
{
    Synapse,
    Signal
}

public sealed record ContractSchema(
    string Fqn,
    ContractKind Kind,
    string[] Fields);

public interface IContractCatalog
{
    void Register(ContractSchema schema);
    bool IsRegistered(string fqn);
    IReadOnlyList<ContractSchema> GetAll();
}

public sealed class InMemoryContractCatalog : IContractCatalog
{
    public static InMemoryContractCatalog? Instance { get; private set; }

    public InMemoryContractCatalog()
    {
        Instance = this;
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ContractSchema> _schemas = new(System.StringComparer.OrdinalIgnoreCase);

    public void Register(ContractSchema schema)
    {
        _schemas[schema.Fqn] = schema;
    }

    public bool IsRegistered(string fqn)
    {
        return _schemas.ContainsKey(fqn);
    }

    public IReadOnlyList<ContractSchema> GetAll()
    {
        return _schemas.Values.ToList();
    }
}
