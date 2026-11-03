using System.Reflection;
using System.Runtime.Loader;

namespace DigitalBrain.Runtime.Apps;

internal sealed class AppLoadContext(string owner) : AssemblyLoadContext("app " + owner, isCollectible: true)
{
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (Default.Assemblies.FirstOrDefault(loaded =>
                AssemblyName.ReferenceMatchesDefinition(assemblyName, loaded.GetName())
                || string.Equals(loaded.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase)) is { } shared)
        {
            return shared;
        }

        if (assemblyName.Name is { Length: > 0 } name)
        {
            var candidate = Path.Combine(AppContext.BaseDirectory, name + ".dll");
            if (File.Exists(candidate))
            {
                return Default.LoadFromAssemblyPath(candidate);
            }
        }

        return null;
    }
}
