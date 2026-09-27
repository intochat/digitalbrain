using System.Reflection;
using DigitalBrain.Contracts;

namespace DigitalBrain.Microsoft.CSharp;

// The neuron contracts scripts may call: interfaces from installed contract assemblies, nothing else.
internal sealed class ScriptContracts(IEnumerable<Assembly> assemblies)
{
    private readonly IReadOnlyDictionary<string, Type> _contracts = assemblies
        .SelectMany(assembly => assembly.GetExportedTypes())
        .Where(type => type.IsInterface && typeof(INeuron).IsAssignableFrom(type) && type != typeof(INeuron))
        .ToDictionary(type => type.FullName!, StringComparer.Ordinal);

    public ScriptContracts() : this(CSharpContractCatalog.ContractAssemblies()) { }

    public Type Find(string name) => _contracts.TryGetValue(name, out var contract)
        ? contract
        : throw new ArgumentException($"{name} is not a neuron contract scripts may call.");
}
