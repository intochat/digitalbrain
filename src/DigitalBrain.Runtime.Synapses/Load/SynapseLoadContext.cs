using System.Reflection;
using System.Runtime.Loader;

namespace DigitalBrain.Runtime.Synapses;

/// <summary>A collectible context for one synapse assembly; framework assemblies resolve from the default context.</summary>
internal sealed class SynapseLoadContext(string name) : AssemblyLoadContext(name, isCollectible: true)
{
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (Default.Assemblies.FirstOrDefault(loaded =>
                AssemblyName.ReferenceMatchesDefinition(assemblyName, loaded.GetName())
                || string.Equals(loaded.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase)) is { } shared)
        {
            return shared;
        }

        if (assemblyName.Name is { Length: > 0 } simple)
        {
            var candidate = Path.Combine(AppContext.BaseDirectory, simple + ".dll");
            if (File.Exists(candidate))
            {
                return Default.LoadFromAssemblyPath(candidate);
            }
        }

        return null;
    }
}
