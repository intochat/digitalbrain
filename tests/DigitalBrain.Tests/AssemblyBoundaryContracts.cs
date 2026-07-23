using System.Reflection;
using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.Aspire;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Client;
using DigitalBrain.Kernel;
using DigitalBrain.Tasks;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class AssemblyBoundaryContracts
{
    private static readonly string[] VendorModelSdks = ["Anthropic", "OpenAI", "Microsoft.Extensions.AI", "OllamaSharp"];

    [Fact(DisplayName = "R-5 gate for Phase 3.5: the kernel assembly reaches no vendor model SDK")]
    public void TheKernelReachesNoVendorModelSdk()
    {
        var reachable = ReachableFrom(typeof(Neuron).Assembly);

        Assert.DoesNotContain(reachable, reference => VendorModelSdks.Any(sdk => reference.StartsWith(sdk, StringComparison.Ordinal)));
    }

    [Fact(DisplayName = "provider SDKs are owned by the AI runtime assembly")]
    public void TheAiRuntimeOwnsProviderSdks()
    {
        var reachable = ReachableFrom(typeof(AIModule).Assembly);

        Assert.Contains(reachable, reference => reference.StartsWith("Microsoft.Extensions.AI", StringComparison.Ordinal));
        Assert.Contains(reachable, reference => reference.StartsWith("OllamaSharp", StringComparison.Ordinal));
        Assert.Contains(reachable, reference => reference.StartsWith("OpenAI", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "public AI runtime types expose MEAI but no MAF implementation types")]
    public void PublicAiRuntimeSurfaceDoesNotExposeMafTypes()
    {
        var exportedSurface = typeof(AIModule).Assembly
            .GetExportedTypes()
            .SelectMany(PublicSurfaceTypes)
            .SelectMany(TypeClosure)
            .Distinct()
            .ToArray();

        Assert.Contains(exportedSurface, type => type == typeof(Microsoft.Extensions.AI.ChatMessage));
        Assert.Contains(exportedSurface, type => type == typeof(Microsoft.Extensions.AI.ChatResponse));
        Assert.DoesNotContain(
            exportedSurface,
            type => type.Namespace?.StartsWith("Microsoft.Agents.AI", StringComparison.Ordinal) is true);
    }

    [Fact]
    public void TheAbstractionsPackageIsALeaf()
        => Assert.DoesNotContain(ReachableFrom(typeof(NeuronId).Assembly), IsDigitalBrain);

    [Fact(DisplayName = "no contracts assembly exposes or references the Kernel delegation transport")]
    public void ContractsDoNotExposeOrReferenceCapabilityDelegation()
    {
        var contracts = new[]
        {
            typeof(INeuron).Assembly,
            typeof(ILLM).Assembly,
            typeof(ITask).Assembly,
        };

        foreach (var assembly in contracts)
        {
            Assert.DoesNotContain(
                assembly.GetReferencedAssemblies(),
                reference => reference.Name == typeof(CapabilityDelegation).Assembly.GetName().Name);
            Assert.DoesNotContain(
                assembly.GetExportedTypes(),
                type => type.FullName == typeof(CapabilityDelegation).FullName);
        }
    }

    [Fact]
    public void TheClientDoesNotReachTheKernel()
        => Assert.DoesNotContain("DigitalBrain.Kernel", ReachableFrom(typeof(DigitalBrainClient).Assembly), StringComparer.Ordinal);

    [Fact]
    public void TheAspireClientIntegrationDoesNotReachTheHostingIntegration()
        => Assert.DoesNotContain(
            ReachableFrom(typeof(DigitalBrainClientHostingExtensions).Assembly),
            reference => reference.StartsWith("Aspire.Hosting", StringComparison.Ordinal));

    [Fact]
    public void TheAspireHostingIntegrationDoesNotReachTheKernel()
        => Assert.DoesNotContain("DigitalBrain.Kernel", ReachableFrom(typeof(BrainService).Assembly), StringComparer.Ordinal);

    private static bool IsDigitalBrain(string assemblyName)
        => assemblyName.StartsWith("DigitalBrain", StringComparison.Ordinal);

    private static IEnumerable<Type> PublicSurfaceTypes(Type type)
    {
        const BindingFlags Members =
            BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Instance
            | BindingFlags.Static
            | BindingFlags.FlattenHierarchy;

        yield return type;

        if (type.BaseType is not null)
        {
            yield return type.BaseType;
        }

        foreach (var contract in type.GetInterfaces())
        {
            yield return contract;
        }

        foreach (var constructor in type.GetConstructors(Members).Where(IsVisible))
        {
            foreach (var parameter in constructor.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }

        foreach (var method in type.GetMethods(Members).Where(IsVisible))
        {
            yield return method.ReturnType;

            foreach (var parameter in method.GetParameters())
            {
                yield return parameter.ParameterType;
            }

            foreach (var argument in method.GetGenericArguments())
            {
                yield return argument;
            }
        }

        foreach (var property in type.GetProperties(Members).Where(IsVisible))
        {
            yield return property.PropertyType;

            foreach (var parameter in property.GetIndexParameters())
            {
                yield return parameter.ParameterType;
            }
        }

        foreach (var field in type.GetFields(Members).Where(IsVisible))
        {
            yield return field.FieldType;
        }

        foreach (var eventInfo in type.GetEvents(Members).Where(IsVisible))
        {
            if (eventInfo.EventHandlerType is not null)
            {
                yield return eventInfo.EventHandlerType;
            }
        }
    }

    private static bool IsVisible(MethodBase method)
        => method.IsPublic
            || method.IsFamily
            || method.IsFamilyOrAssembly
            || method.IsFamilyAndAssembly;

    private static bool IsVisible(FieldInfo field)
        => field.IsPublic
            || field.IsFamily
            || field.IsFamilyOrAssembly
            || field.IsFamilyAndAssembly;

    private static bool IsVisible(PropertyInfo property)
        => property.GetAccessors(nonPublic: true).Any(IsVisible);

    private static bool IsVisible(EventInfo eventInfo)
        => new[]
        {
            eventInfo.AddMethod,
            eventInfo.RemoveMethod,
            eventInfo.RaiseMethod,
        }
        .OfType<MethodInfo>()
        .Any(IsVisible);

    private static IEnumerable<Type> TypeClosure(Type type)
    {
        var pending = new Stack<Type>([type]);
        var reached = new HashSet<Type>();

        while (pending.TryPop(out var current))
        {
            if (!reached.Add(current))
            {
                continue;
            }

            yield return current;

            if (current.HasElementType && current.GetElementType() is { } element)
            {
                pending.Push(element);
            }

            foreach (var argument in current.GetGenericArguments())
            {
                pending.Push(argument);
            }

            if (current.IsGenericParameter)
            {
                foreach (var constraint in current.GetGenericParameterConstraints())
                {
                    pending.Push(constraint);
                }
            }
        }
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
                    pending.Enqueue(Load(reference, root));
                }
            }
        }

        return reached;
    }

    private static Assembly Load(AssemblyName reference, Assembly root)
    {
        try
        {
            return Assembly.Load(reference);
        }
        catch (Exception failure) when (failure is FileNotFoundException or FileLoadException or BadImageFormatException)
        {
            throw new InvalidOperationException(
                $"'{reference.Name}' is reachable from {root.GetName().Name} but could not be loaded, so the boundary below it is unverifiable. "
                + "Treating it as a leaf would let a forbidden reference hide behind it.",
                failure);
        }
    }
}
