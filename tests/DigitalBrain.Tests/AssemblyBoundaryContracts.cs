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
    private static readonly string[] VendorModelSdks =
        ["Anthropic", "OpenAI", "Microsoft.Extensions.AI", "OllamaSharp"];

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
            .Where(name => name != "DigitalBrain.Testing")
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

    [Fact]
    public void ContractsDoNotReferenceKernel()
    {
        foreach (var assembly in new[]
                 {
                     typeof(INeuron).Assembly,
                     typeof(ILLM).Assembly,
                     typeof(ITask).Assembly,
                 })
        {
            Assert.DoesNotContain(
                assembly.GetReferencedAssemblies(),
                reference => reference.Name == "DigitalBrain.Kernel");
        }
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
