using System.Xml.Linq;

namespace DigitalBrain.Tests.Architecture;

public sealed class OrleansPackageFamilyTests
{
    [Fact]
    public void Orleans_packages_are_one_stable_family_without_journaling()
    {
        var root = RepositoryRoot();
        var props = XDocument.Load(Path.Combine(root, "Directory.Packages.props"));
        var packages = props.Descendants("PackageVersion")
            .Select(element => new
            {
                Name = (string?)element.Attribute("Include"),
                Version = (string?)element.Attribute("Version")
            })
            .Where(static package => package.Name?.StartsWith("Microsoft.Orleans.", StringComparison.Ordinal) is true)
            .ToArray();

        Assert.NotEmpty(packages);
        Assert.All(packages, static package => Assert.Equal("10.2.1", package.Version));
        Assert.DoesNotContain(packages, static package => package.Name!.Contains("Journaling", StringComparison.Ordinal));

        var projectReferences = Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(path => XDocument.Load(path).Descendants("PackageReference"))
            .Select(static element => (string?)element.Attribute("Include"))
            .Where(static name => name?.StartsWith("Microsoft.Orleans.", StringComparison.Ordinal) is true)
            .ToArray();
        Assert.DoesNotContain(projectReferences, static name => name!.Contains("Journaling", StringComparison.Ordinal));
    }

    [Fact]
    public void Production_sources_and_topology_do_not_use_the_experimental_journal_rail()
    {
        var root = RepositoryRoot();
        var productionRoots = new[] { "src", "hosts" };
        var forbidden = new[]
        {
            "Orleans.Journaling",
            "DurableGrain",
            "IDurableList<",
            "ConnectionStrings__journal",
            "AddBlobs(\"journal\")",
            "JournalBlobs"
        };
        var violations = productionRoots
            .SelectMany(directory => Directory.EnumerateFiles(
                Path.Combine(root, directory),
                "*",
                SearchOption.AllDirectories))
            .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(static path => Path.GetExtension(path) is ".cs" or ".csproj" or ".json")
            .Where(path => forbidden.Any(value => File.ReadAllText(path).Contains(value, StringComparison.Ordinal)))
            .Select(path => Path.GetRelativePath(root, path))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(violations);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Brain.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
