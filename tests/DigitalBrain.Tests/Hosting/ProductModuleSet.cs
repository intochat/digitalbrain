using DigitalBrain.Tests.Packages;
using Xunit;

namespace DigitalBrain.Tests.Hosting;

public sealed class ProductModuleSet
{
    [Fact(DisplayName = "evaluated repository graph is 52 projects, 157 references, 76 packages, 32 IDs, and 32 pins")]
    public void EvaluatedRepositoryGraphHasNoPackageOrProjectResidue()
    {
        var graph = PackageInventory.EvaluateRepositoryGraph();

        Assert.Equal(52, graph.ProjectCount);
        Assert.Equal(157, graph.ProjectReferenceCount);
        Assert.Equal(76, graph.PackageReferenceCount);
        Assert.Equal(32, graph.PackageIdCount);
        Assert.Equal(32, graph.CentralPackageVersionCount);
    }

    [Fact(DisplayName = "product host, AppHost, and hosting families select AI, Flutter, Google, and Salesforce only")]
    public void ProductModuleSelectionIsExactAcrossAllProductSurfaces()
    {
        var graph = PackageInventory.EvaluateRepositoryGraph();

        Assert.Equal(
            PackageInventory.ProductRuntimeModules,
            graph.DirectProjectReferencesOf(PackageInventory.ProductSiloHost)
                .Where(PackageInventory.IsModuleRuntime)
                .Order(StringComparer.Ordinal));
        Assert.Equal(
            PackageInventory.ProductHostingModules,
            graph.DirectProjectReferencesOf(PackageInventory.ProductAppHost)
                .Where(PackageInventory.IsModuleHosting)
                .Order(StringComparer.Ordinal));
        Assert.Equal(
            PackageInventory.ProductHostingModules,
            graph.ProjectNames
                .Where(PackageInventory.IsModuleHosting)
                .Order(StringComparer.Ordinal));
    }

    [Fact(DisplayName = "Tasks and Time remain valid modules without product hosting selection")]
    public void TasksAndTimeArePresentButUnselectedProducts()
    {
        var graph = PackageInventory.EvaluateRepositoryGraph();

        Assert.Contains(PackageInventory.ModulesTasks, graph.ProjectNames);
        Assert.Contains(PackageInventory.ModulesTime, graph.ProjectNames);
        Assert.DoesNotContain(PackageInventory.ModulesTasks, PackageInventory.ProductRuntimeModules);
        Assert.DoesNotContain(PackageInventory.ModulesTime, PackageInventory.ProductRuntimeModules);
        Assert.DoesNotContain(PackageInventory.ModulesTasksAspireHosting, graph.ProjectNames);
        Assert.DoesNotContain(PackageInventory.ModulesTimeAspireHosting, graph.ProjectNames);
    }
}
