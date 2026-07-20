using System.Reflection;
using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.Aspire;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Client;
using DigitalBrain.Kernel;
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

    [Fact]
    public void TheAbstractionsPackageIsALeaf()
        => Assert.DoesNotContain(ReachableFrom(typeof(NeuronId).Assembly), IsDigitalBrain);

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
