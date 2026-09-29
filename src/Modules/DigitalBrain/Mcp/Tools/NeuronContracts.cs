using System.Reflection;
using DigitalBrain.Contracts;

namespace DigitalBrain.Mcp;

public sealed class NeuronContracts
{
    private readonly Dictionary<string, Type> _contracts;

    public NeuronContracts(IEnumerable<Assembly> assemblies)
    {
        _contracts = assemblies
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Where(type => type.IsInterface && type != typeof(INeuron) && typeof(INeuron).IsAssignableFrom(type))
            .ToDictionary(type => type.FullName!, StringComparer.Ordinal);
    }

    public IReadOnlyCollection<string> Names => _contracts.Keys;

    public Type Find(string name) => _contracts.TryGetValue(name, out var contract)
        ? contract
        : throw new ArgumentException($"{name} is not a loaded neuron contract. Pass the full type name from neurons_list.");

    public static Assembly[] Loaded() => [typeof(INeuron).Assembly, typeof(DigitalBrain.Time.Timers.ITimer).Assembly];
}
