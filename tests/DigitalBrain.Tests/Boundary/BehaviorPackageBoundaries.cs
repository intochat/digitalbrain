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
}
