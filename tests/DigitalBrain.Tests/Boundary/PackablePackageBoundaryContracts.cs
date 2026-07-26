using DigitalBrain.Tests.Packages;
using Xunit;

namespace DigitalBrain.Tests.Boundary;

public sealed class PackablePackageBoundaryContracts
{
    [Fact]
    public void PackableProjectsMatchTheDeclaredInventory()
    {
        var actual = RepositoryLayout.PackableTreeRoots
            .SelectMany(root => Directory.EnumerateFiles(
                Path.Combine(RepositoryLayout.Root, root),
                "*.csproj",
                SearchOption.AllDirectories))
            .Where(path => !RepositoryLayout.IsIgnoredLookupPath(path))
            .Where(PackageBoundarySupport.IsPackable)
            .Select(Path.GetFileNameWithoutExtension!)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(
            PackableProjects.Names.Order(StringComparer.Ordinal),
            actual.Order(StringComparer.Ordinal));
    }
}
