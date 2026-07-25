using Xunit;

namespace DigitalBrain.Tests.Boundary;

public sealed class KernelPackageBoundaryContracts
{
    public static TheoryData<string> ConsumerPathPackages { get; } =
        [.. PackageBoundarySupport.ConsumerPath];

    public static TheoryData<string> HostingPackages { get; } =
        [.. PackageBoundarySupport.HostingPackages];

    [Theory]
    [MemberData(nameof(ConsumerPathPackages))]
    public void NothingOnTheConsumerPathCanReachTheKernel(string package)
    {
        Assert.DoesNotContain(
            "DigitalBrain.Kernel",
            PackageBoundarySupport.ProjectsReachableFrom(package),
            StringComparer.Ordinal);
    }

    [Theory]
    [MemberData(nameof(HostingPackages))]
    public void HostingPackagesDoNotDirectlyReferenceKernel(string package)
    {
        Assert.DoesNotContain(
            "DigitalBrain.Kernel",
            PackageBoundarySupport.DirectCompileProjectReferencesOf(package),
            StringComparer.Ordinal);
        Assert.DoesNotContain(
            "DigitalBrain.Kernel",
            PackageBoundarySupport.DirectPackageReferencesOf(package),
            StringComparer.Ordinal);
    }

    [Fact(DisplayName = "Kernel compile graph is Abstractions only — never Flutter, Ui, or modules")]
    public void KernelCompileGraphIsAbstractionsOnly()
    {
        Assert.Equal(
            ["DigitalBrain.Abstractions"],
            PackageBoundarySupport.DirectCompileProjectReferencesOf("DigitalBrain.Kernel")
                .Order(StringComparer.Ordinal));

        var reachable = PackageBoundarySupport.CompileProjectsReachableFrom("DigitalBrain.Kernel");
        Assert.DoesNotContain(
            reachable,
            project => project.Contains("Flutter", StringComparison.OrdinalIgnoreCase)
                || project is "DigitalBrain.Ui"
                || project.StartsWith("DigitalBrain.Ui.", StringComparison.Ordinal)
                || project.StartsWith("DigitalBrain.Modules.", StringComparison.Ordinal));
    }
}
