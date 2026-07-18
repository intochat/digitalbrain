namespace DigitalBrain.Core;

// Marks an assembly as part of the InoLang contract surface so the kernel's
// AssemblyScanningContractCatalog discovers it during a load-time scan.
// Replaces the brittle `.Contracts` suffix convention E-RUN #34 left behind:
// the suffix would let a future `DigitalBrain.Foo.SharedTypes` (or a Marketplace
// bundle) declare a `Synapse`-derived record that the linker silently fails
// to find (INO300), even though the record exists and the assembly is
// loaded. With this attribute, contract assemblies opt in explicitly and
// the discovery neuron survives renames and external bundles.
[AttributeUsage(AttributeTargets.Assembly, Inherited = false, AllowMultiple = false)]
public sealed class ContractAssemblyAttribute : Attribute;
