using System.Xml.Linq;

namespace DigitalBrain.Architecture.Tests;

public sealed class RepositoryLayoutFacts
{
    [Fact]
    public void EveryProjectHasOneSolutionEntryInItsPhysicalGroup()
    {
        var root = RepositoryRoot();
        var solution = XDocument.Load(Path.Combine(root, "DigitalBrain.slnx"));
        var entries = solution.Descendants("Project").ToArray();
        var paths = entries.Select(entry => entry.Attribute("Path")!.Value.Replace('\\', '/')).ToArray();
        Assert.Equal(paths.Length, paths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        var disk = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "obj" or "bin"))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/')).Order().ToArray();
        Assert.Equal(disk, paths.Order().ToArray(), OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var path = entry.Attribute("Path")!.Value.Replace('\\', '/');
            var directory = Path.GetDirectoryName(path)!.Replace('\\', '/');
            var name = Path.GetFileNameWithoutExtension(path);
            Assert.Equal(name, Path.GetFileName(directory));
            var group = directory[..directory.LastIndexOf('/')];
            Assert.Equal("/" + group["src/".Length..] + "/", entry.Parent!.Attribute("Name")?.Value);
            if (name.EndsWith(".Tests", StringComparison.Ordinal) || name.Contains(".Tests.", StringComparison.Ordinal))
            {
                Assert.Equal("tests", Path.GetFileName(group));
            }
            var project = XDocument.Load(Path.Combine(root, path));
            foreach (var reference in project.Descendants("ProjectReference"))
            {
                var include = reference.Attribute("Include")!.Value;
                if (include.Contains('@', StringComparison.Ordinal)) { continue; }
                Assert.True(File.Exists(Path.Combine(root, directory, include.Replace('\\', '/'))), $"Broken project reference: {path} -> {include}");
            }
            foreach (var property in project.Descendants().Where(element => element.Name.LocalName is "RootNamespace" or "AssemblyName" or "PackageId"))
            {
                Assert.Equal(name, property.Value);
            }
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DigitalBrain.slnx")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new DirectoryNotFoundException("DigitalBrain.slnx was not found.");
    }
}
