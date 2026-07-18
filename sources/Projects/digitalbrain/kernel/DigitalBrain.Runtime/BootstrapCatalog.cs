using DigitalBrain.InoLang.Linking;

namespace DigitalBrain.Runtime;

// The smallest catalog the Linker needs to resolve Genesis's three ports.
// Not the full runtime catalog — the steady-mode cluster catalog is E-RUN.
//
// E-RUN #42 reservation. The three FQNs below are *boot-floor pseudo-FQNs* —
// reserved names the Genesis .ino binds to, not the FullName of any concrete
// C# type. `DigitalBrain.Boot` and `DigitalBrain.SDK.Aspire` are namespaces; the
// real SDK Aspire neuron grain carries [GrainType("DigitalBrain.SDK.Aspire.
// Runtime")] (note the .Runtime suffix), and `DigitalBrain.DomainInstalled` has no
// C# counterpart at all. Keeping these names disjoint from anything the
// steady-state AssemblyScanningContractCatalog would reflect lets the boot
// and runtime catalogs coexist without one silently shadowing the other.
//
// Two safeguards lock the property in:
//   1. LayeredContractCatalog (DigitalBrain.InoLang.Linking) is the
//      structural primitive a caller wraps both catalogs behind so the
//      primary's reserved entries short-circuit before the fallback ever
//      runs. BootHost.RunFromFileAsync still passes only BootstrapCatalog.
//      Default today — every future path that compiles InoLang against a
//      union catalog uses the layered shape.
//   2. BootstrapCatalogReservationInvariantTests (DigitalBrain.Kernel.Tests/
//      Runtime) asserts that no concrete reflected type, [GrainType] id, or
//      [Signal] FQN in the broad kernel-tests AppDomain collides with the
//      names below — locking in the reservation even for callers that wire
//      only one catalog. Add new names here in the same PR that extends the
//      test's enumeration.
public sealed class BootstrapCatalog : IContractCatalog
{
    public static BootstrapCatalog Default { get; } = new();

    static readonly Dictionary<string, ContractSchema> Schemas =
        new(StringComparer.Ordinal)
        {
            ["DigitalBrain.Kernel.Loaded"] =
                new("DigitalBrain.Kernel.Loaded", ContractKind.Synapse, []),
            ["DigitalBrain.BrainRegistry"] =
                new("DigitalBrain.BrainRegistry", ContractKind.Neuron, []),
            ["DigitalBrain.BrainCreated"] =
                new("DigitalBrain.BrainCreated", ContractKind.Synapse, ["brainId"]),
        };

    public ContractSchema? Resolve(string fqn) => Schemas.GetValueOrDefault(fqn);

    public IReadOnlyCollection<ContractSchema> GetAllSchemas() => Schemas.Values;

    public void Register(ContractSchema schema) => Schemas[schema.Fqn] = schema;
}
