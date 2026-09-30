using System.Reflection;

namespace DigitalBrain.Core;

// The modules selected for this host, available to modules without coupling hosting to their consumers.
public sealed class ModuleInventory
{
    public ModuleInventory(IEnumerable<Type> moduleTypes)
    {
        ArgumentNullException.ThrowIfNull(moduleTypes);
        Types = Array.AsReadOnly(moduleTypes.ToArray());
    }

    public IReadOnlyList<Type> Types { get; }

    // Every contract assembly this composition exposes: each module's ".Contracts" companion (or the
    // module assembly itself when it has none), plus the kernel contracts. The one discovery for
    // everything that answers "which neuron contracts exist here" - registry, script edge, MCP alike.
    public IReadOnlyList<Assembly> ContractAssemblies()
        => [.. Types.Select(module => module.Assembly).Distinct()
            .SelectMany(ContractAssembliesOf)
            .Prepend(typeof(Contracts.INeuron).Assembly)
            .Distinct()];

    public static IReadOnlyList<Assembly> ContractAssembliesOf(Assembly module)
    {
        ArgumentNullException.ThrowIfNull(module);
        var contracts = module.GetReferencedAssemblies()
            .Where(reference => reference.Name == module.GetName().Name + ".Contracts")
            .Select(Assembly.Load).ToArray();
        return contracts.Length == 0 ? [module] : contracts;
    }
}
