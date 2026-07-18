using System.Collections.Concurrent;
using System.Reflection;
using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime;

public sealed class SynapsePayloadRegistry
{
    readonly ConcurrentDictionary<string, Type> _byName = new(StringComparer.Ordinal);

    public void Register(Type type)
    {
        if (type.FullName is null)
            throw new ArgumentException("Type must have a FullName.", nameof(type));
        _byName[type.FullName] = type;
    }

    public bool TryResolve(string typeName, out Type type)
        => _byName.TryGetValue(typeName, out type!);

    // Discover every concrete public Synapse-derived type across loaded DigitalBrain.*
    // assemblies and register it by FQN. Each Contracts assembly must be loaded
    // before this runs — callers typically force that by referencing one type
    // from each (a `_ = typeof(...)` touchpoint does the trick).
    public int RegisterDiscoveredSynapses()
    {
        var before = _byName.Count;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var name = asm.GetName().Name;
            if (name is null || (!name.StartsWith("DigitalBrain.", StringComparison.Ordinal) && !name.StartsWith("DigitalBrain.", StringComparison.Ordinal))) continue;

            foreach (var t in SafeGetTypes(asm))
            {
                if (t is { IsClass: true, IsAbstract: false, IsPublic: true }
                    && typeof(Synapse).IsAssignableFrom(t))
                {
                    Register(t);
                }
            }
        }
        return _byName.Count - before;
    }

    static IEnumerable<Type> SafeGetTypes(Assembly a)
    {
        try
        {
            return a.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }
}
