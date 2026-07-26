using DigitalBrain.Tests.Packages;
using Xunit;

namespace DigitalBrain.Tests.Boundary;

public sealed class BehaviorPackageBoundaries
{
    [Fact(DisplayName = "Behavior SDK is Abstractions-only; builder and runtime never flow into module contracts")]
    public void BehaviorPackageBoundariesAreOneWay()
    {
        Assert.Equal(
            [PackageInventory.Abstractions],
            PackageBoundarySupport.DirectCompileProjectReferencesOf(PackageInventory.Behaviors));
        Assert.Empty(
            PackageBoundarySupport.DirectPackageReferencesOf(PackageInventory.Behaviors));
    }

    [Fact(DisplayName = "Behavior SDK is one of 29 evaluated packages; Runtime is nonpackable and Builder is absent")]
    public void BehaviorPackageInventoryMatchesTheEvaluatedRepository()
    {
        var graph = PackageInventory.EvaluateRepositoryGraph();

        Assert.Equal(29, graph.PackableProjectNames.Count);
        Assert.Equal(
            PackageInventory.Packable.Order(StringComparer.Ordinal),
            graph.PackableProjectNames.Order(StringComparer.Ordinal));
        Assert.Contains(PackageInventory.Behaviors, graph.PackableProjectNames);
        Assert.Contains(PackageInventory.BehaviorsRuntime, graph.ProjectNames);
        Assert.DoesNotContain(PackageInventory.BehaviorsRuntime, graph.PackableProjectNames);
        Assert.Equal(
            [PackageInventory.Behaviors],
            graph.DirectProjectReferencesOf(PackageInventory.BehaviorsRuntime)
                .Order(StringComparer.Ordinal));
        Assert.DoesNotContain("DigitalBrain.BehaviorBuilder", graph.ProjectNames);
    }
}
