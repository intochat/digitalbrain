using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using DigitalBrain.Tests.Packages;
using Xunit;

namespace DigitalBrain.Tests.Boundary;

public sealed class KernelPackageBoundaryContracts
{
    private static readonly string Kernel = PackageOf(typeof(Neuron));
    private static readonly string Abstractions = PackageOf(typeof(NeuronId));

    public static TheoryData<string> ConsumerPathPackages { get; } =
        [.. PackageBoundarySupport.ConsumerPath];

    public static TheoryData<string> HostingPackages { get; } =
        [.. PackageBoundarySupport.HostingPackages];

    [Theory]
    [MemberData(nameof(ConsumerPathPackages))]
    public void NothingOnTheConsumerPathCanReachTheKernel(string package)
    {
        Assert.DoesNotContain(
            Kernel,
            PackageBoundarySupport.ProjectsReachableFrom(package),
            StringComparer.Ordinal);
    }

    [Theory]
    [MemberData(nameof(HostingPackages))]
    public void HostingPackagesDoNotDirectlyReferenceKernel(string package)
    {
        Assert.DoesNotContain(
            Kernel,
            PackageBoundarySupport.DirectCompileProjectReferencesOf(package),
            StringComparer.Ordinal);
        Assert.DoesNotContain(
            Kernel,
            PackageBoundarySupport.DirectPackageReferencesOf(package),
            StringComparer.Ordinal);
    }

    [Fact(DisplayName = "Kernel compile graph is Abstractions only — never Flutter, Ui, or modules")]
    public void KernelCompileGraphIsAbstractionsOnly()
    {
        Assert.Equal(
            [Abstractions],
            PackageBoundarySupport.DirectCompileProjectReferencesOf(Kernel)
                .Order(StringComparer.Ordinal));

        var reachable = PackageBoundarySupport.CompileProjectsReachableFrom(Kernel);
        Assert.DoesNotContain(
            reachable,
            project => project.Contains("Flutter", StringComparison.OrdinalIgnoreCase)
                || PackageInventory.IsUiFamilyProject(project)
                || PackageInventory.IsModulesProject(project));
    }

    private static string PackageOf(Type type)
        => type.Assembly.GetName().Name
           ?? throw new InvalidOperationException($"Assembly for {type.FullName} has no name.");
}
