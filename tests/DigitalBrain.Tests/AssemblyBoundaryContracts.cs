using System.Reflection;
using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.AI.Aspire.Hosting;
using DigitalBrain.Aspire;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Client;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Aspire.Hosting;
using DigitalBrain.Google;
using DigitalBrain.Google.Aspire.Hosting;
using DigitalBrain.Kernel;
using DigitalBrain.Quickstart;
using DigitalBrain.Salesforce;
using DigitalBrain.Salesforce.Aspire.Hosting;
using DigitalBrain.Tasks;
using DigitalBrain.Time;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class AssemblyBoundaryContracts
{
    private static readonly string[] VendorModelSdks =
        ["OpenAI", "Microsoft.Extensions.AI", "OllamaSharp"];

    private static readonly Assembly[] ContractAssemblies =
    [
        typeof(ILLM).Assembly,
        typeof(IGmail).Assembly,
        typeof(ISalesforce).Assembly,
        typeof(ITask).Assembly,
        typeof(ICountdown).Assembly,
        typeof(IShell).Assembly,
        typeof(IGreeter).Assembly,
    ];

    private static readonly Assembly[] HostingAssemblies =
    [
        typeof(DigitalBrainBuilder).Assembly,
        typeof(AIHostingExtensions).Assembly,
        typeof(FlutterHostingExtensions).Assembly,
        typeof(GoogleHostingExtensions).Assembly,
        typeof(SalesforceHostingExtensions).Assembly,
    ];

    [Fact(DisplayName = "the kernel assembly reaches no vendor model SDK")]
    public void TheKernelReachesNoVendorModelSdk()
    {
        var reachable = ReachableFrom(typeof(Neuron).Assembly);
        Assert.DoesNotContain(
            reachable,
            reference => VendorModelSdks.Any(sdk =>
                reference.StartsWith(sdk, StringComparison.Ordinal)));
    }

    [Fact(DisplayName = "provider SDKs are owned by the AI runtime assembly")]
    public void TheAiRuntimeOwnsProviderSdks()
    {
        var reachable = ReachableFrom(typeof(AIModule).Assembly);
        Assert.Contains(reachable, r => r.StartsWith("Microsoft.Extensions.AI", StringComparison.Ordinal));
        Assert.Contains(reachable, r => r.StartsWith("OllamaSharp", StringComparison.Ordinal));
        Assert.Contains(reachable, r => r.StartsWith("OpenAI", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "public packable types do not export MAF implementation types")]
    public void PublicPackableTypesDoNotExportMafTypes()
    {
        var mafExports = PackableProjects.Names
            .Where(name => name is not ("DigitalBrain" or "DigitalBrain.Testing"))
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

    [Fact(DisplayName = "every contracts assembly is free of Kernel")]
    public void ContractsDoNotReferenceKernel()
    {
        foreach (var assembly in ContractAssemblies)
        {
            Assert.DoesNotContain(
                assembly.GetReferencedAssemblies(),
                reference => reference.Name == "DigitalBrain.Kernel");
        }
    }

    [Fact(DisplayName = "every contracts assembly is free of Dart and Flutter SDK assemblies")]
    public void ContractsAreFreeOfDartAndFlutterSdks()
    {
        foreach (var assembly in ContractAssemblies)
        {
            var references = assembly
                .GetReferencedAssemblies()
                .Select(reference => reference.Name!)
                .ToArray();

            Assert.DoesNotContain(
                references,
                name => name.StartsWith("Dart", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(
                references,
                name => name.Contains("Flutter", StringComparison.OrdinalIgnoreCase)
                    && !name.StartsWith("DigitalBrain.Modules.Flutter", StringComparison.Ordinal)
                    && !name.StartsWith("DigitalBrain.Flutter", StringComparison.Ordinal));
        }
    }

    [Fact(DisplayName = "every Aspire.Hosting assembly is free of a direct Kernel reference")]
    public void HostingAssembliesDoNotReferenceKernelDirectly()
    {
        foreach (var assembly in HostingAssemblies)
        {
            Assert.DoesNotContain(
                assembly.GetReferencedAssemblies(),
                reference => reference.Name == "DigitalBrain.Kernel");
        }
    }

    [Fact(DisplayName = "Aspire.Hosting public API carries no Kernel types in signatures or constraints")]
    public void HostingPublicApiIsFreeOfKernelTypes()
    {
        var offenders = HostingAssemblies
            .SelectMany(KernelTypesInPublicApi)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact(DisplayName = "the kernel assembly reaches no Flutter module, Dart SDK, or UI host")]
    public void TheKernelReachesNoFlutterModuleOrDartSdk()
    {
        var reachable = ReachableFrom(typeof(Neuron).Assembly);
        Assert.DoesNotContain(
            reachable,
            reference => reference.Contains("Flutter", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            reachable,
            reference => reference.StartsWith("Dart", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            reachable,
            reference => reference is "DigitalBrain.Ui"
                || reference.StartsWith("DigitalBrain.Ui.", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "the kernel assembly carries no Flutter or UI surface vocabulary")]
    public void TheKernelCarriesNoFlutterOrUiVocabulary()
    {
        string[] forbiddenNameFragments =
        [
            "Flutter",
            "IShell",
            "IScene",
            "OpenScene",
            "SceneOpened",
            "ControlActivated",
            "UiGateway",
            "UiSurface",
            "BuildContext",
            "Widget",
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
    public void TheClientDoesNotReachTheKernel()
        => Assert.DoesNotContain(
            "DigitalBrain.Kernel",
            ReachableFrom(typeof(DigitalBrainClient).Assembly),
            StringComparer.Ordinal);

    [Fact]
    public void TheAspireClientIntegrationDoesNotReachHosting()
        => Assert.DoesNotContain(
            ReachableFrom(typeof(DigitalBrainClientHostingExtensions).Assembly),
            reference => reference.StartsWith("Aspire.Hosting", StringComparison.Ordinal));

    [Fact]
    public void TheAspireHostingIntegrationDoesNotReachTheKernel()
        => Assert.DoesNotContain(
            "DigitalBrain.Kernel",
            ReachableFrom(typeof(DigitalBrainBuilder).Assembly),
            StringComparer.Ordinal);

    private static bool IsDigitalBrain(string assemblyName)
        => assemblyName.StartsWith("DigitalBrain", StringComparison.Ordinal);

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
        if (type.IsGenericType)
        {
            if (type.GetGenericArguments().Any(IsKernelType))
            {
                return true;
            }
        }

        return type.Assembly.GetName().Name == "DigitalBrain.Kernel"
            || type.Namespace?.StartsWith("DigitalBrain.Kernel", StringComparison.Ordinal) is true;
    }

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
