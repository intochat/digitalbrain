namespace DigitalBrain.InoLang.Linking;

// E-RUN #42. A two-layer IContractCatalog: the primary owns a small reserved
// surface (today: DigitalBrain.Boot's three boot-floor pseudo-FQNs) and resolves it
// authoritatively; everything else falls through to the fallback (today: the
// kernel's AssemblyScanningContractCatalog over `*.Contracts`). A primary hit
// short-circuits the fallback, so a boot-floor name can never be silently
// shadowed by a same-named reflected type even when both catalogs are wrapped
// behind the same IContractCatalog the Linker compiles against.
//
// Composition primitive, not boot-specific. Any future caller that needs to
// stack a reserved-vocabulary catalog over the production one (Marketplace
// bundles overlaying the local catalog, identity-scoped contract overlays)
// uses this same shape — the fix for the architectural neuron #42 calls out.
public sealed class LayeredContractCatalog : IContractCatalog
{
    readonly IContractCatalog _primary;
    readonly IContractCatalog _fallback;

    public LayeredContractCatalog(IContractCatalog primary, IContractCatalog fallback)
    {
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(fallback);
        _primary = primary;
        _fallback = fallback;
    }

    public ContractSchema? Resolve(string fqn) =>
        _primary.Resolve(fqn) ?? _fallback.Resolve(fqn);

    public IReadOnlyCollection<ContractSchema> GetAllSchemas()
    {
        var merged = new Dictionary<string, ContractSchema>(StringComparer.Ordinal);
        foreach (var schema in _fallback.GetAllSchemas())
        {
            merged[schema.Fqn] = schema;
        }
        foreach (var schema in _primary.GetAllSchemas())
        {
            merged[schema.Fqn] = schema;
        }
        return merged.Values;
    }

    public void Register(ContractSchema schema)
    {
        _primary.Register(schema);
        _fallback.Register(schema);
    }
}
