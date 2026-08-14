using System.Xml.Linq;
using Xunit;

namespace Brain.Architecture.Tests;

public sealed class NoV1CompilationBoundaryTests
{
    [Fact]
    public void Compiled_projects_never_reference_a_v1_project()
    {
        var references = ProjectReferenceScanner.ReadAll("DigitalBrain.slnx");

        Assert.DoesNotContain(references, path => path.Contains("src/Kernel/", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(references, path => path.Contains("src/Modules/", StringComparison.OrdinalIgnoreCase));
    }
}

internal static class ProjectReferenceScanner
{
    internal static IReadOnlyList<string> ReadAll(string solutionPath)
    {
        var root = RepositoryRoot.Find();
        var solution = XDocument.Load(Path.Combine(root, solutionPath));
        var projects = solution
            .Descendants("Project")
            .Select(project => project.Attribute("Path")?.Value)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => path!.Replace('\\', '/'))
            .ToArray();
        var references = new List<string>(projects);

        foreach (var project in projects)
        {
            var projectPath = Path.GetFullPath(Path.Combine(root, project));
            var projectDirectory = Path.GetDirectoryName(projectPath)!;
            var document = XDocument.Load(projectPath);

            references.AddRange(document
                .Descendants("ProjectReference")
                .Select(reference => reference.Attribute("Include")?.Value)
                .Where(static path => !string.IsNullOrWhiteSpace(path))
                .Select(path => Path.GetRelativePath(root, Path.GetFullPath(Path.Combine(projectDirectory, path!)))
                    .Replace('\\', '/')));
        }

        return references;
    }
}

internal static class RepositoryRoot
{
    internal static string Find()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DigitalBrain.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the repository root containing DigitalBrain.slnx.");
    }
}
