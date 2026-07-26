using System.Reflection;
using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.Aspire;
using DigitalBrain.Flutter;
using DigitalBrain.Kernel;
using DigitalBrain.Tests.Packages;
using Xunit;

namespace DigitalBrain.Tests.Boundary;

public sealed class AssemblyBoundaryContracts
{
    private static readonly Assembly[] HostingAssemblies =
        [.. PackageBoundarySupport.HostingPackages.Select(Assembly.Load)];

    private static readonly string KernelPackage = PackageOf(typeof(Neuron));

    private static readonly string KernelNamespace =
        typeof(Neuron).Namespace
        ?? throw new InvalidOperationException($"{nameof(Neuron)} has no namespace.");

    [Fact(DisplayName = "the kernel assembly reaches no vendor model SDK, Dart, or Flutter SDK")]
    public void TheKernelReachesNoVendorModelSdkOrFlutterDartSdk()
    {
        var reachable = ReachableFrom(typeof(Neuron).Assembly);

        Assert.DoesNotContain(reachable, IsVendorModelSdkAssembly);
        Assert.DoesNotContain(reachable, PackageBoundarySupport.IsDartOrFlutterSdkPackage);
    }

    [Fact(DisplayName = "provider SDKs are owned by the AI runtime assembly")]
    public void TheAiRuntimeOwnsProviderSdks()
    {
        var reachable = ReachableFrom(typeof(AIModule).Assembly);

        foreach (var prefix in PackageBoundarySupport.ProviderSdkPrefixes)
        {
            if (prefix is "ModelContextProtocol")
            {
                continue;
            }

            Assert.Contains(
                reachable,
                reference => reference.StartsWith(prefix, StringComparison.Ordinal));
        }
    }

    [Fact(DisplayName = "public packable types do not export MAF implementation types")]
    public void PublicPackableTypesDoNotExportMafTypes()
    {
        var mafExports = PackableProjects.Names
            .Where(name => name is not (PackageInventory.Metapackage or PackageInventory.Testing))
            .Select(Assembly.Load)
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Where(type => type.Namespace?.StartsWith(
                "Microsoft.Agents.AI",
                StringComparison.Ordinal) is true)
            .Select(type => type.FullName)
            .ToArray();

        Assert.Empty(mafExports);
    }

    [Fact]
    public void TheAbstractionsPackageIsALeaf()
        => Assert.DoesNotContain(ReachableFrom(typeof(NeuronId).Assembly), IsDigitalBrain);

    [Fact(DisplayName = "Aspire.Hosting public API carries no Kernel types in signatures or constraints")]
    public void HostingPublicApiIsFreeOfKernelTypes()
    {
        var offenders = HostingAssemblies
            .SelectMany(KernelTypesInPublicApi)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact(DisplayName = "the kernel assembly carries no Flutter or UI surface vocabulary")]
    public void TheKernelCarriesNoFlutterOrUiVocabulary()
    {
        string[] forbiddenNameFragments =
        [
            "Flutter",
            "UiGateway",
            "UiSurface",
            "BuildContext",
            "Widget",
            .. typeof(IShell).Assembly
                .GetExportedTypes()
                .Where(type => type.Namespace == typeof(IShell).Namespace)
                .Select(type => type.Name),
        ];

        var offenders = typeof(Neuron).Assembly
            .GetTypes()
            .Select(type => type.FullName!)
            .Where(fullName => forbiddenNameFragments.Any(fragment =>
                fullName.Contains(fragment, StringComparison.Ordinal)))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void TheAspireClientIntegrationDoesNotReachHosting()
        => Assert.DoesNotContain(
            ReachableFrom(typeof(DigitalBrainClientHostingExtensions).Assembly),
            reference => reference.StartsWith("Aspire.Hosting", StringComparison.Ordinal));

    private static bool IsVendorModelSdkAssembly(string assemblyName)
        => PackageBoundarySupport.ProviderSdkPrefixes.Any(prefix =>
               assemblyName.StartsWith(prefix, StringComparison.Ordinal))
           || assemblyName.StartsWith("Microsoft.Extensions.AI", StringComparison.Ordinal);

    private static bool IsDigitalBrain(string assemblyName)
        => assemblyName.StartsWith(PackageInventory.Metapackage, StringComparison.Ordinal);

    private static IEnumerable<string> KernelTypesInPublicApi(Assembly assembly)
    {
        foreach (var type in assembly.GetExportedTypes())
        {
            foreach (var method in type.GetMethods(
                         BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                foreach (var candidate in TypesInMethod(method))
                {
                    if (IsKernelType(candidate))
                    {
                        yield return $"{assembly.GetName().Name}:{type.Name}.{method.Name} uses {candidate.FullName}";
                    }
                }
            }

            foreach (var property in type.GetProperties(
                         BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (IsKernelType(property.PropertyType))
                {
                    yield return $"{assembly.GetName().Name}:{type.Name}.{property.Name} uses {property.PropertyType.FullName}";
                }
            }
        }
    }

    private static IEnumerable<Type> TypesInMethod(MethodInfo method)
    {
        yield return method.ReturnType;

        foreach (var parameter in method.GetParameters())
        {
            yield return parameter.ParameterType;
        }

        foreach (var argument in method.GetGenericArguments())
        {
            foreach (var constraint in argument.GetGenericParameterConstraints())
            {
                yield return constraint;
                for (var bas = constraint.BaseType; bas is not null; bas = bas.BaseType)
                {
                    yield return bas;
                }
            }
        }
    }

    private static bool IsKernelType(Type type)
    {
        if (type.IsGenericType && type.GetGenericArguments().Any(IsKernelType))
        {
            return true;
        }

        return type.Assembly.GetName().Name == KernelPackage
            || type.Namespace?.StartsWith(KernelNamespace, StringComparison.Ordinal) is true;
    }

    private static string PackageOf(Type type)
        => type.Assembly.GetName().Name
           ?? throw new InvalidOperationException($"Assembly for {type.FullName} has no name.");

    private static HashSet<string> ReachableFrom(Assembly root)
    {
        var reached = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<Assembly>([root]);

        while (pending.Count > 0)
        {
            foreach (var reference in pending.Dequeue().GetReferencedAssemblies())
            {
                if (reached.Add(reference.Name!))
                {
                    pending.Enqueue(Assembly.Load(reference));
                }
            }
        }

        return reached;
    }
}
