using System.Xml.Linq;

namespace DigitalBrain.Core.Tests.Unit;

public sealed class ProjectReferenceFacts
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DigitalBrain.slnx")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName
            ?? throw new InvalidOperationException($"Repository root (DigitalBrain.slnx) was not found above {AppContext.BaseDirectory}.");
    }

    private static string PathInRepo(string relative) => Path.Combine(RepositoryRoot, relative.Replace('/', Path.DirectorySeparatorChar));

    private static string Read(string relative) => File.ReadAllText(PathInRepo(relative));

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
        || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void EveryProjectReferenceResolvesToAnExistingFile()
    {
        var broken = new List<string>();
        foreach (var project in Directory.EnumerateFiles(PathInRepo("src"), "*.csproj", SearchOption.AllDirectories))
        {
            if (IsBuildOutput(project)) { continue; }
            var document = XDocument.Load(project);
            foreach (var reference in document.Descendants("ProjectReference"))
            {
                var include = reference.Attribute("Include")?.Value;
                if (string.IsNullOrWhiteSpace(include)) { continue; }
                var target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project)!, include));
                if (!File.Exists(target))
                {
                    broken.Add($"{Path.GetRelativePath(RepositoryRoot, project)} -> {include}");
                }
            }
        }
        Assert.Empty(broken);
    }

}
