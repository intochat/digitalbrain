using System.Reflection;
using DigitalBrain.Runtime.Runtime;
using DigitalBrain.InoLang.Linking;

namespace DigitalBrain.Kernel.Runtime;

// E-RUN #44. Symmetric startup invariant to AssemblyScanningContractCatalog
// (#41): every concrete neuron-target grain that claims an InoLang-addressable
// [GrainType("Fqn")] must resolve in the production IContractCatalog as
// ContractKind.Neuron. The catalog neuron scan and this verifier walk the
// same loaded set, so the two cannot drift while both run against
// AssemblyScanningContractCatalog — the value is locking the invariant in
// for the DI-customized case (an alternate catalog impl, a tests-only
// substitution) and for [GrainType] strings that drift from `.ino`
// neuron(...) references over time.
//
// Pure form (no Orleans cluster, no DI). The hosted-service adapter
// NeuronCatalogInvariantHostedService calls this at silo StartAsync and
// promotes a non-empty Result into a thrown exception so the silo refuses
// gateway traffic until the substrate matches.
//
// Scope today is the forward direction (neuron grain ⇒ catalog Neuron entry).
// The reverse (catalog Neuron ⇒ neuron grain exists) needs catalog
// enumeration, which IContractCatalog deliberately does not expose; once a
// real driver appears (E1 marketplace bundles producing non-neuron Neuron
// entries is the candidate), the reverse pass lands behind that ABI change.
public static class NeuronCatalogInvariantVerifier
{
    public sealed record Violation(string TypeFullName, string GrainTypeFqn, string Reason);

    public sealed record Result(IReadOnlyList<Violation> Violations)
    {
        public bool IsValid => Violations.Count == 0;
    }

    public static Result Verify(IContractCatalog catalog, IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        return Verify(catalog, assemblies.SelectMany(SafeGetTypes));
    }

    public static Result Verify(IContractCatalog catalog, IEnumerable<Type> candidateTypes)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(candidateTypes);

        var violations = new List<Violation>();
        foreach (var type in candidateTypes)
        {
            if (!IsConcreteNeuronTarget(type)) continue;
            // No [GrainType] means Orleans falls back to a default-derived id
            // that is not addressable from `using $port = neuron(...)`. The
            // catalog neuron scan skips the same case (GetGrainTypeFqn returns
            // null); mirroring it here keeps the two scans symmetric.
            if (GetGrainTypeFqn(type) is not { } fqn) continue;

            var schema = catalog.Resolve(fqn);
            if (schema is null)
            {
                violations.Add(new Violation(
                    type.FullName ?? type.Name,
                    fqn,
                    $"Neuron grain {type.FullName} declares [GrainType(\"{fqn}\")] but the catalog has no entry — InoLang `neuron({fqn})` would link red against this catalog."));
                continue;
            }
            if (schema.Kind != ContractKind.Neuron)
            {
                violations.Add(new Violation(
                    type.FullName ?? type.Name,
                    fqn,
                    $"Neuron grain {type.FullName} declares [GrainType(\"{fqn}\")] but the catalog records {fqn} as ContractKind.{schema.Kind}, not Neuron — a same-named Synapse or Signal record is shadowing the neuron."));
            }
        }
        return new Result(violations);
    }

    // Mirrors AssemblyScanningContractCatalog.IsConcreteNeuronTarget so the two
    // scans cover the same union. Open-generic types are excluded because
    // Orleans cannot activate them until closed.
    static bool IsConcreteNeuronTarget(Type type) =>
        type.IsClass
        && !type.IsAbstract
        && !type.ContainsGenericParameters
        && (typeof(ICallNeuronTarget).IsAssignableFrom(type)
            || typeof(IStreamNeuronTarget).IsAssignableFrom(type)
            || typeof(IResourceNeuronTarget).IsAssignableFrom(type)
            || typeof(IPredicateNeuronTarget).IsAssignableFrom(type));

    // Read-by-name (not typeof) matches the catalog's pattern and keeps the
    // verifier independent of Orleans's attribute load order.
    static string? GetGrainTypeFqn(Type type) =>
        type.GetCustomAttributesData()
            .FirstOrDefault(a => a.AttributeType.FullName == "Orleans.GrainTypeAttribute")
            ?.ConstructorArguments.FirstOrDefault().Value as string;

    static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null)!; }
    }
}
