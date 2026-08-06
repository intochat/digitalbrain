using System.Reflection;

namespace DigitalBrain;

internal static class ModuleAssemblyBoundary
{
    private const string AccessAssembly = "DigitalBrain.Access";
    private const string HostingAssembly = "DigitalBrain.Hosting";

    internal static void Validate(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        Validate(assembly.GetName().Name ?? assembly.FullName ?? "<unknown>", assembly.GetReferencedAssemblies());
    }

    internal static void Validate(string moduleName, IEnumerable<AssemblyName> references)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleName);
        ArgumentNullException.ThrowIfNull(references);

        foreach (var reference in references)
        {
            var name = reference.Name;
            if (name?.Contains("Orleans", StringComparison.Ordinal) == true)
            {
                throw new InvalidOperationException(
                    $"Module assembly '{moduleName}' directly references '{name}'. "
                    + "Only DigitalBrain.Hosting may reference Orleans.");
            }

            if (string.Equals(name, AccessAssembly, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Module assembly '{moduleName}' directly references {AccessAssembly}. "
                    + "Behavior modules do not acquire publication or journal-read capabilities.");
            }

            if (string.Equals(name, HostingAssembly, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Module assembly '{moduleName}' directly references {HostingAssembly}. "
                    + "Hosting owns the durable adapter and is not a module dependency.");
            }
        }
    }
}
