using System.Reflection;
using DigitalBrain;
using DigitalBrain.Contracts;

namespace DigitalBrain.Microsoft.CSharp;

// The neuron contracts scripts may call: interfaces from the composed modules' contract
// assemblies, nothing else. Composing a module is what exposes its contracts to scripts.
internal sealed class ScriptContracts(IEnumerable<Assembly> assemblies)
{
    private readonly IReadOnlyDictionary<string, Type> _contracts = ContractVocabulary.VisibleTypes(assemblies)
        .Where(ContractVocabulary.IsNeuron)
        // Double filter (assembly + per-type) is deliberate defense-in-depth: credential-bearing contracts protected at both levels.
        .ToDictionary(type => type.FullName!, StringComparer.Ordinal);

    public Type Find(string name) => _contracts.TryGetValue(name, out var contract)
        ? contract
        : throw new ArgumentException($"{name} is not a neuron contract scripts may call.");
}
