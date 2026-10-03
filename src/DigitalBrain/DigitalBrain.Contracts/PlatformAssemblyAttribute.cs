using System.Reflection;

namespace DigitalBrain.Contracts;

// Marks an assembly as the platform-only ring: none of its contracts are ever script-visible, whatever
// their own attributes say. Applied once in DigitalBrain.Platform; honored wherever contracts are discovered.
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class PlatformAssemblyAttribute : Attribute
{
    public static bool IsPlatform(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return assembly.IsDefined(typeof(PlatformAssemblyAttribute), inherit: false);
    }
}
