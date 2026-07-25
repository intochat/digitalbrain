using Xunit;

using DigitalBrain.Tests.Packages;

namespace DigitalBrain.Tests.Boundary;

public sealed class PackablePackageBoundaryContracts
{
    [Fact]
    public void PackableProjectsMatchTheDeclaredInventory()
    {
        var actual = PackageBoundarySupport.ProductionRoots
            .SelectMany(root => Directory.EnumerateFiles(
                Path.Combine(PackageBoundarySupport.RepositoryRoot, root),
                "*.csproj",
                SearchOption.AllDirectories))
            .Where(path => !PackageBoundarySupport.IsIgnoredLookupPath(path))
            .Where(PackageBoundarySupport.IsPackable)
            .Select(Path.GetFileNameWithoutExtension!)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(
            PackableProjects.Names.Order(StringComparer.Ordinal),
            actual.Order(StringComparer.Ordinal));
    }
}
