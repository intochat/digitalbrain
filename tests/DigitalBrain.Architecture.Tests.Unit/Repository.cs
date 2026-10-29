using System.Xml.Linq;

namespace DigitalBrain.Architecture.Tests.Unit;

// The one place a fact learns where the repository is. DigitalBrain.slnx is the map.
internal static class Repository
{
    internal const string SolutionFile = "DigitalBrain.slnx";

    internal sealed record Project(string Name, string Path)
    {
        public string Directory => System.IO.Path.GetDirectoryName(Path)!;
    }

    internal static readonly string Root = FindRoot();

    internal static readonly IReadOnlyDictionary<string, Project> Projects = XDocument.Load(System.IO.Path.Combine(Root, SolutionFile))
        .Descendants("Project")
        .Select(entry => entry.Attribute("Path")!.Value.Replace('\\', '/'))
        .Select(relative => new Project(System.IO.Path.GetFileNameWithoutExtension(relative), System.IO.Path.GetFullPath(System.IO.Path.Combine(Root, relative))))
        .ToDictionary(project => project.Name, StringComparer.Ordinal);

    internal static Project ProjectNamed(string name)
        => Projects.TryGetValue(name, out var project) ? project
            : throw new KeyNotFoundException($"{name} is not a project in {SolutionFile}.");

    internal static IEnumerable<string> SourceFiles(string directory, string pattern)
        => !System.IO.Directory.Exists(directory) ? []
            : System.IO.Directory.EnumerateFiles(directory, pattern, SearchOption.AllDirectories)
                .Where(path => !path.Split(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar).Any(part => part is "obj" or "bin"));

    internal static IEnumerable<string> ProjectReferences(string csproj)
        => XDocument.Load(csproj).Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .OfType<string>()
            .Select(include => System.IO.Path.GetFileNameWithoutExtension(include.Replace('\\', '/')));

    internal static IEnumerable<string> PackageReferences(string csproj)
        => XDocument.Load(csproj).Descendants("PackageReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .OfType<string>();

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(System.IO.Path.Combine(dir.FullName, SolutionFile))) { return dir.FullName; }
        }
        throw new InvalidOperationException($"{SolutionFile} not found above {AppContext.BaseDirectory}.");
    }
}
