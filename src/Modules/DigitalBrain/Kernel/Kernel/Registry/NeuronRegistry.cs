using System.Reflection;
using System.Text.RegularExpressions;
using DigitalBrain.Contracts;

namespace DigitalBrain.Core.Registry;

public sealed record NeuronContract(string Id, Type Interface, string ModuleId)
{
    public string Name => Interface.Name is { Length: > 1 } name && name[0] == 'I' && char.IsUpper(name[1])
        ? name[1..] : Interface.Name;

    public string SearchText => Regex.Replace(string.Join(' ', new[] { Id, Name }
        .Concat(Interface.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => method.Name))), "(?<=[a-z])(?=[A-Z])", " ");
}

public sealed class NeuronRegistry(IEnumerable<Type> moduleTypes)
{
    private readonly Type[] _moduleTypes = [.. moduleTypes];
    private IReadOnlyList<NeuronContract>? _contracts;

    public IReadOnlyList<NeuronContract> All => _contracts
        ?? throw new InvalidOperationException("Neuron discovery has not completed.");

    public NeuronContract? Find(string id) => All.FirstOrDefault(contract => contract.Id == id);

    internal void Discover()
    {
        var byId = new Dictionary<string, NeuronContract>(StringComparer.Ordinal);
        foreach (var moduleType in _moduleTypes)
        {
            var assembly = moduleType.Assembly;
            var contractAssemblies = assembly.GetReferencedAssemblies()
                .Where(reference => reference.Name == assembly.GetName().Name + ".Contracts")
                .Select(Assembly.Load)
                .ToArray();
            // Test modules can declare their contracts in the same assembly as the module.
            if (contractAssemblies.Length == 0) { contractAssemblies = [assembly]; }
            foreach (var contractAssembly in contractAssemblies)
            {
                foreach (var type in contractAssembly.GetExportedTypes())
                {
                    if (!type.IsInterface || type == typeof(INeuron) || !typeof(INeuron).IsAssignableFrom(type))
                    { continue; }
                    var id = type.GetCustomAttribute<AliasAttribute>()?.Alias;
                    if (string.IsNullOrWhiteSpace(id))
                    { throw new InvalidOperationException($"Public neuron contract {type.FullName} requires an Orleans alias."); }
                    if (byId.TryGetValue(id, out var existing))
                    {
                        if (existing.Interface != type)
                        { throw new InvalidOperationException($"Duplicate neuron alias '{id}'."); }
                        continue;
                    }
                    byId.Add(id, new NeuronContract(id, type, moduleType.FullName!));
                }
            }
        }
        _contracts = Array.AsReadOnly(byId.Values.OrderBy(contract => contract.Id, StringComparer.Ordinal).ToArray());
    }
}
