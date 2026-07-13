using System.Diagnostics;
using System.Xml.Linq;

namespace DigitalBrain.Tests.Architecture;

public sealed class ProjectDependencyTests
{
    private static readonly HashSet<(string Source, string Target)> ForbiddenEdges =
    [
        ("DigitalBrain.Kernel", "DigitalBrain.Mcp"),
        ("DigitalBrain.Kernel", "DigitalBrain.Google"),
        ("DigitalBrain.Kernel", "DigitalBrain.Salesforce"),
        ("DigitalBrain.Kernel", "DigitalBrain.Ui.Contracts"),
        ("DigitalBrain.Kernel", "DigitalBrain.Ui.Runtime"),
        ("DigitalBrain.Kernel", "DigitalBrain.ServiceDefaults"),
        ("DigitalBrain.Google", "DigitalBrain.Kernel"),
        ("DigitalBrain.Salesforce", "DigitalBrain.Kernel")
    ];

    [Fact]
    public void Tracked_projects_do_not_cross_independent_boundaries()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectPaths = GetTrackedProjectPaths(repositoryRoot);
        var violations = projectPaths
            .SelectMany(projectPath => ReadProjectEdges(repositoryRoot, projectPath))
            .Where(ForbiddenEdges.Contains)
            .OrderBy(edge => edge.Source, StringComparer.Ordinal)
            .ThenBy(edge => edge.Target, StringComparer.Ordinal)
            .Select(edge => $"{edge.Source} -> {edge.Target}")
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Forbidden project references:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Brain.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private static string[] GetTrackedProjectPaths(string repositoryRoot)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("ls-files");
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add("*.csproj");
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start git.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, error);
        return output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
    }

    private static IEnumerable<(string Source, string Target)> ReadProjectEdges(
        string repositoryRoot,
        string projectPath)
    {
        var source = Path.GetFileNameWithoutExtension(projectPath);
        var document = XDocument.Load(Path.Combine(repositoryRoot, projectPath));
        return document
            .Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => (source, Path.GetFileNameWithoutExtension(path!)));
    }
}
