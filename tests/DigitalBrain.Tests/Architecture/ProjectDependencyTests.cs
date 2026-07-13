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
        var projectPaths = GetTrackedPaths(repositoryRoot, "*.csproj");
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

    [Fact]
    public void Deployment_publishes_the_runtime_host_instead_of_the_kernel_library()
    {
        var repositoryRoot = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(repositoryRoot, ".github", "workflows", "deploy.yml"));

        Assert.Contains(
            "dotnet publish hosts/DigitalBrain.RuntimeHost/DigitalBrain.RuntimeHost.csproj",
            workflow,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "dotnet publish src/DigitalBrain.Kernel/DigitalBrain.Kernel.csproj",
            workflow,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Kernel_hosting_does_not_register_browser_cors()
    {
        var repositoryRoot = FindRepositoryRoot();
        var hostingSource = string.Join(
            Environment.NewLine,
            GetTrackedPaths(repositoryRoot, "src/DigitalBrain.Kernel/Hosting/*.cs")
                .Select(path => File.ReadAllText(Path.Combine(repositoryRoot, path))));

        Assert.DoesNotContain("AddCors(", hostingSource, StringComparison.Ordinal);
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

    private static string[] GetTrackedPaths(string repositoryRoot, string pattern)
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
        startInfo.ArgumentList.Add(pattern);
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
