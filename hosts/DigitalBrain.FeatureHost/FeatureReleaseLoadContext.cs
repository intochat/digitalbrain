using System.Reflection;
using System.Runtime.Loader;

namespace DigitalBrain.FeatureHost;

internal sealed class FeatureReleaseLoadContext : AssemblyLoadContext
{
    private static readonly HashSet<string> TrustedPlatformAssemblies = GetTrustedPlatformAssemblies();
    private readonly AssemblyDependencyResolver _resolver;
    private readonly string _implementationDirectory;
    private readonly IReadOnlyDictionary<string, Assembly> _sharedAssemblies;

    public FeatureReleaseLoadContext(string implementationAssemblyPath, IEnumerable<Assembly> sharedAssemblies)
        : base($"digitalbrain-feature-{Path.GetFileNameWithoutExtension(implementationAssemblyPath)}-{Guid.NewGuid():N}", isCollectible: true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(implementationAssemblyPath);
        ArgumentNullException.ThrowIfNull(sharedAssemblies);
        var fullPath = Path.GetFullPath(implementationAssemblyPath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("The Feature implementation assembly does not exist.", fullPath);
        _implementationDirectory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("The implementation assembly requires a parent directory.", nameof(implementationAssemblyPath));
        _resolver = new AssemblyDependencyResolver(fullPath);
        _sharedAssemblies = sharedAssemblies.ToDictionary(
            assembly => assembly.GetName().Name ?? throw new ArgumentException("Shared assemblies require a simple name.", nameof(sharedAssemblies)),
            StringComparer.OrdinalIgnoreCase);
        if (_sharedAssemblies.Values.Any(assembly => GetLoadContext(assembly) != Default))
            throw new ArgumentException("Shared Feature assemblies must be loaded in the default context.", nameof(sharedAssemblies));
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name is null)
            return null;
        if (_sharedAssemblies.TryGetValue(assemblyName.Name, out var shared))
            return shared;
        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        if (path is null && TrustedPlatformAssemblies.Contains(assemblyName.Name))
            return null;
        if (path is null)
            throw new FileNotFoundException($"Feature dependency '{assemblyName.Name}' is neither private nor explicitly shared.");
        return LoadFromAssemblyPath(ContainedPath(path));
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? 0 : LoadUnmanagedDllFromPath(ContainedPath(path));
    }

    private string ContainedPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var relative = Path.GetRelativePath(_implementationDirectory, fullPath);
        if (Path.IsPathRooted(relative) || relative.Equals("..", StringComparison.Ordinal) ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new FileLoadException("Feature dependencies must resolve within the immutable release directory.", fullPath);
        return fullPath;
    }

    private static HashSet<string> GetTrustedPlatformAssemblies()
    {
        var paths = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        var runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location);
        return paths is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : paths.Split(Path.PathSeparator).Where(path => string.Equals(Path.GetDirectoryName(path), runtimeDirectory, StringComparison.OrdinalIgnoreCase))
                .Select(Path.GetFileNameWithoutExtension)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToHashSet(StringComparer.OrdinalIgnoreCase)!;
    }
}
