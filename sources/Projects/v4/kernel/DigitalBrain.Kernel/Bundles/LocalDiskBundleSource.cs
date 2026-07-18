using System.Reflection;
using DigitalBrain.Abstractions.Bundles;

namespace DigitalBrain.Kernel.Bundles;

public sealed class LocalDiskBundleSource(string? baseDirectory = null) : IBundleSource
{
    private readonly string _baseDirectory = baseDirectory ?? AppContext.BaseDirectory;

    public IReadOnlyList<IBundle> LoadBundles()
    {
        Console.WriteLine($"[LocalDiskBundleSource] Scanning directory: {_baseDirectory}");
        foreach (var assemblyPath in Directory.EnumerateFiles(_baseDirectory, "DigitalBrain.*.dll"))
        {
            try
            {
                Console.WriteLine($"[LocalDiskBundleSource] Loading assembly: {Path.GetFileName(assemblyPath)}");
                Assembly.LoadFrom(assemblyPath);
            }
            catch (BadImageFormatException ex)
            {
                Console.WriteLine($"[LocalDiskBundleSource] BadImageFormatException loading {assemblyPath}: {ex.Message}");
            }
            catch (FileLoadException ex)
            {
                Console.WriteLine($"[LocalDiskBundleSource] FileLoadException loading {assemblyPath}: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LocalDiskBundleSource] Exception loading {assemblyPath}: {ex.Message}");
            }
        }

        return AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(SafeGetTypes)
            .Where(type => typeof(IBundle).IsAssignableFrom(type)
                && type is { IsAbstract: false, IsInterface: false }
                && type.GetConstructor(Type.EmptyTypes) is not null)
            .Select(type => (IBundle)Activator.CreateInstance(type)!)
            .ToList();
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.OfType<Type>();
        }
    }
}
