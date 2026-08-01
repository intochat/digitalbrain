using System.Reflection;
using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.Google;
using DigitalBrain.Memory;
using DigitalBrain.Salesforce;
using DigitalBrain.Tasks;
using Xunit;

namespace DigitalBrain.Tests.Contracts;

public sealed class ProhibitedSurfaces
{
    [Fact(DisplayName = "shipped assemblies never export KernelTask or WorkId")]
    public void NoKernelTaskOrWorkIdExports()
    {
        var offenders = ShippedAssemblies()
            .SelectMany(static assembly => assembly.GetExportedTypes())
            .Where(static type => type.Name is "KernelTask" or "WorkId"
                || (type.FullName?.Contains("KernelTask", StringComparison.Ordinal) is true)
                || (type.FullName?.Contains("WorkId", StringComparison.Ordinal) is true))
            .Select(static type => type.FullName!)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact(DisplayName = "shipped assemblies never export IIntentProgram")]
    public void NoIIntentProgramExport()
    {
        var offenders = ShippedAssemblies()
            .SelectMany(static assembly => assembly.GetExportedTypes())
            .Where(static type =>
                type.Name.StartsWith("IIntentProgram", StringComparison.Ordinal)
                || (type.FullName?.Contains("IIntentProgram", StringComparison.Ordinal) is true))
            .Select(static type => type.FullName!)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact(DisplayName = "Agent has no ToolsFor override surface; AdditionalToolsFor is the local-only hook")]
    public void AgentHasNoToolsForSurface()
    {
        var methods = typeof(Agent)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Select(static method => method.Name)
            .ToArray();

        Assert.DoesNotContain("ToolsFor", methods);
        Assert.Equal("Agent", typeof(Agent).Name);
        Assert.NotNull(typeof(Agent).GetMethod(
            "AdditionalToolsFor",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public));
    }

    [Fact(DisplayName = "IGmail and ISalesforce remain marker neurons with no operation methods")]
    public void ProviderNeuronsHaveNoOperationMethods()
    {
        Assert.Empty(DeclaredMembers(typeof(IGmail)));
        Assert.Empty(DeclaredMembers(typeof(ISalesforce)));
        Assert.Contains(typeof(INeuron), typeof(IGmail).GetInterfaces());
        Assert.Contains(typeof(INeuron), typeof(ISalesforce).GetInterfaces());
    }

    [Fact(DisplayName = "IVectorMemory is a marker neuron with no operation methods")]
    public void VectorMemoryIsMarkerNeuron()
    {
        Assert.Empty(DeclaredMembers(typeof(IVectorMemory)));
        Assert.Contains(typeof(INeuron), typeof(IVectorMemory).GetInterfaces());
    }

    [Fact(DisplayName = "ITask keeps only Start Cancel Read lifecycle methods")]
    public void TaskSurfaceIsLifecycleOnly()
    {
        var methods = typeof(ITask)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(static method => method.Name)
            .Where(static name => name is not "Start" and not "Cancel" and not "Read"
                && !name.StartsWith("get_", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(methods);
    }

    [Fact(DisplayName = "shipped public surface has no ReadRecentMessages or ReadMessage operation APIs")]
    public void NoReadRecentMessagesOrReadMessagePublicApis()
    {
        var offenders = ShippedAssemblies()
            .SelectMany(static assembly => assembly.GetExportedTypes())
            .SelectMany(static type => type
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Select(method => $"{type.FullName}.{method.Name}"))
            .Where(static name =>
                name.EndsWith(".ReadRecentMessages", StringComparison.Ordinal)
                || name.EndsWith(".ReadMessage", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offenders);
    }

    private static IEnumerable<MemberInfo> DeclaredMembers(Type type)
        => type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(static member => member is not MethodInfo { IsSpecialName: true });

    private static List<Assembly> ShippedAssemblies()
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<Assembly>([typeof(ProhibitedSurfaces).Assembly]);
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
        => assemblyName.StartsWith("DigitalBrain.", StringComparison.Ordinal)
        && !assemblyName.EndsWith(".Testing", StringComparison.Ordinal)
        && !assemblyName.EndsWith(".Tests", StringComparison.Ordinal);
}
