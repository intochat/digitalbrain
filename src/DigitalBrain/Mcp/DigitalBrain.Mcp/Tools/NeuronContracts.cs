using System.Reflection;
using DigitalBrain;
using DigitalBrain.Contracts;

namespace DigitalBrain.Mcp;

public sealed class NeuronContracts
{
    private readonly Dictionary<string, Type> _contracts;

    public NeuronContracts(IEnumerable<Assembly> assemblies)
    {
        // Double filter (assembly + per-type) is deliberate defense-in-depth: credential-bearing contracts protected at both levels.
        _contracts = assemblies
            .Where(assembly => !PlatformAssemblyAttribute.IsPlatform(assembly))
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Where(type => type.IsInterface && type != typeof(INeuron) && typeof(INeuron).IsAssignableFrom(type) && !PlatformOnlyAttribute.AppliesTo(type))
            .ToDictionary(type => type.FullName!, StringComparer.Ordinal);
    }

    public IReadOnlyCollection<string> Names => _contracts.Keys;

    public Type Find(string name) => _contracts.TryGetValue(name, out var contract)
        ? contract
        : throw new ArgumentException($"{name} is not a loaded neuron contract. Pass the full type name from neurons_list.");

    // This server is a brain client, so its addressable surface is whatever contract assemblies are
    // deployed beside it: every referenced DigitalBrain.Modules.*.Contracts plus the kernel's.
    public static Assembly[] Deployed() =>
    [
        typeof(INeuron).Assembly,
        .. Directory.GetFiles(AppContext.BaseDirectory, "DigitalBrain.Modules.*.Contracts.dll")
            .Select(path => Assembly.Load(AssemblyName.GetAssemblyName(path))),
    ];
}
