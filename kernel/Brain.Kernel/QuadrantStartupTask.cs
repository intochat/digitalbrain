using System.Reflection;
using Orleans.Runtime;

namespace Brain.Kernel;

public sealed class QuadrantStartupTask : IStartupTask
{
    private readonly Quadrant _quadrant;
    private readonly IClusterManifestProvider _manifestProvider;
    private readonly Func<IEnumerable<Type>> _typeSource;

    public QuadrantStartupTask(
        Quadrant quadrant,
        IClusterManifestProvider manifestProvider)
        : this(quadrant, manifestProvider, CollectLoadedApplicationTypes)
    {
    }

    public QuadrantStartupTask(
        Quadrant quadrant,
        IClusterManifestProvider manifestProvider,
        Func<IEnumerable<Type>> typeSource)
    {
        _quadrant = quadrant;
        _manifestProvider = manifestProvider;
        _typeSource = typeSource;
    }

    public Task Execute(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var registrations = NeuronTypeCatalogBuilder.Build(_typeSource());
        OrleansNeuronManifestValidator.Validate(
            registrations,
            _manifestProvider.LocalGrainManifest);
        _quadrant.Load(registrations);
        return Task.CompletedTask;
    }

    private static IEnumerable<Type> CollectLoadedApplicationTypes()
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .Where(IsApplicationAssembly)
            .SelectMany(GetLoadableTypes)
            .Where(type => type.IsPublic || type.IsNestedPublic);
    }

    private static bool IsApplicationAssembly(Assembly assembly)
    {
        if (assembly.IsDynamic)
            return false;

        var name = assembly.GetName().Name;
        if (string.IsNullOrEmpty(name))
            return false;

        if (name.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase))
            return false;

        if (name.StartsWith("Microsoft.", StringComparison.Ordinal)
            || name.StartsWith("System.", StringComparison.Ordinal)
            || name.StartsWith("Orleans", StringComparison.Ordinal)
            || name.StartsWith("xunit", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Azure.", StringComparison.Ordinal)
            || name.StartsWith("Newtonsoft.", StringComparison.Ordinal)
            || name.StartsWith("OpenTelemetry", StringComparison.Ordinal)
            || name.StartsWith("Polly", StringComparison.Ordinal)
            || name.StartsWith("Humanizer", StringComparison.Ordinal)
            || name.StartsWith("testhost", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.OfType<Type>();
        }
    }
}
