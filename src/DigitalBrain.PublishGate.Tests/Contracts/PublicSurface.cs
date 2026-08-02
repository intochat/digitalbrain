using System.Reflection;
using DigitalBrain.Kernel;
using DigitalBrain.Shell;
using Xunit;

namespace DigitalBrain.Tests.Contracts;

public sealed class PublicSurface
{
    private const string MafNamespace = "Microsoft.Agents.AI";

    private static readonly string KernelAssemblyName = NameOf(typeof(Neuron).Assembly);

    private static readonly string KernelNamespace =
        typeof(Neuron).Namespace
        ?? throw new InvalidOperationException($"{nameof(Neuron)} has no namespace.");

    [Fact(DisplayName = "no shipped package exports a Microsoft Agent Framework type")]
    public void NoShippedPackageExportsAgentFrameworkTypes()
    {
        var leaked = ShippedAssemblies()
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Where(type => type.Namespace?.StartsWith(MafNamespace, StringComparison.Ordinal) is true)
            .Select(type => type.FullName)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(leaked);
    }

    [Fact(DisplayName = "the kernel assembly is inside the shipped set the publish gate scans")]
    public void TheKernelAssemblyIsInsideTheShippedSet()
        => Assert.Contains(ShippedAssemblies(), assembly => NameOf(assembly) == KernelAssemblyName);

    [Fact(DisplayName = "Aspire hosting exposes no Kernel type in a public signature")]
    public void AspireHostingExposesNoKernelTypeInPublicSignatures()
    {
        var hosting = ShippedAssemblies()
            .Where(assembly => NameOf(assembly).Contains(".Aspire.Hosting", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(hosting);

        var leaked = hosting
            .SelectMany(KernelTypesInPublicApiOf)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(leaked);
    }

    [Fact(DisplayName = "the kernel declares no user-interface vocabulary")]
    public void TheKernelDeclaresNoUserInterfaceVocabulary()
    {
        string[] forbidden =
        [
            "Flutter",
            "UIGateway",
            "UISurface",
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
            .Where(name => forbidden.Any(fragment => name.Contains(fragment, StringComparison.Ordinal)))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
    }

    private static List<Assembly> ShippedAssemblies()
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<Assembly>([typeof(PublicSurface).Assembly]);
        var shipped = new List<Assembly>();

        while (pending.Count > 0)
        {
            foreach (var reference in pending.Dequeue().GetReferencedAssemblies())
            {
                if (!visited.Add(reference.Name!))
                {
                    continue;
                }

                var assembly = Assembly.Load(reference);
                pending.Enqueue(assembly);

                if (IsShipped(reference.Name!))
                {
                    shipped.Add(assembly);
                }
            }
        }

        Assert.NotEmpty(shipped);
        return shipped;
    }

    private static bool IsShipped(string assemblyName)
        => (assemblyName == KernelAssemblyName
            || assemblyName.StartsWith("DigitalBrain.", StringComparison.Ordinal))
        && !assemblyName.EndsWith(".Testing", StringComparison.Ordinal)
        && !assemblyName.EndsWith(".Tests", StringComparison.Ordinal);

    private static IEnumerable<string> KernelTypesInPublicApiOf(Assembly assembly)
    {
        foreach (var type in assembly.GetExportedTypes())
        {
            const BindingFlags declared =
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

            foreach (var method in type.GetMethods(declared))
            {
                foreach (var used in TypesUsedBy(method).Where(IsKernelType))
                {
                    yield return $"{NameOf(assembly)}:{type.Name}.{method.Name} uses {used.FullName}";
                }
            }

            foreach (var property in type.GetProperties(declared).Where(p => IsKernelType(p.PropertyType)))
            {
                yield return $"{NameOf(assembly)}:{type.Name}.{property.Name} uses {property.PropertyType.FullName}";
            }
        }
    }

    private static IEnumerable<Type> TypesUsedBy(MethodInfo method)
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

                for (var baseType = constraint.BaseType; baseType is not null; baseType = baseType.BaseType)
                {
                    yield return baseType;
                }
            }
        }
    }

    private static bool IsKernelType(Type type)
        => (type.IsGenericType && type.GetGenericArguments().Any(IsKernelType))
        || NameOf(type.Assembly) == KernelAssemblyName
        || type.Namespace?.StartsWith(KernelNamespace, StringComparison.Ordinal) is true;

    private static string NameOf(Assembly assembly)
        => assembly.GetName().Name
        ?? throw new InvalidOperationException("Assembly has no name.");
}
