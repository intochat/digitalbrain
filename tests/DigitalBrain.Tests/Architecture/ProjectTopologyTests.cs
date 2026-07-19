using System.Xml.Linq;
using Brain.Kernel.Host;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace DigitalBrain.Tests.Architecture;

public sealed class ProjectTopologyTests
{
    [Fact]
    public void Active_solution_contains_exactly_the_durable_neuron_foundation_projects()
    {
        var solutionPath = FindRepositoryFile("Brain.slnx");
        var projectPaths = XDocument.Load(solutionPath)
            .Descendants("Project")
            .Select(element => element.Attribute("Path")?.Value)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "hosts/Brain.Kernel.Host/Brain.Kernel.Host.csproj",
                "hosts/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj",
                "integrations/DigitalBrain.Aspire.Hosting/DigitalBrain.Aspire.Hosting.csproj",
                "integrations/DigitalBrain.Aspire/DigitalBrain.Aspire.csproj",
                "integrations/DigitalBrain.DevTools/DigitalBrain.DevTools.csproj",
                "kernel/DigitalBrain.Abstractions/DigitalBrain.Abstractions.csproj",
                "kernel/DigitalBrain.Client/DigitalBrain.Client.csproj",
                "kernel/DigitalBrain.Kernel/DigitalBrain.Kernel.csproj",
                "modules/Google.Contracts/Google.Contracts.csproj",
                "modules/Google/Google.csproj",
                "modules/Salesforce.Contracts/Salesforce.Contracts.csproj",
                "modules/Salesforce/Salesforce.csproj",
                "tests/Brain.FeasibilityTests/Brain.FeasibilityTests.csproj",
                "tests/DigitalBrain.PackageTests/DigitalBrain.PackageTests.csproj",
                "tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj"
            ],
            projectPaths);

        var projectNames = projectPaths
            .Select(path => Path.GetFileNameWithoutExtension(path)!)
            .ToArray();

        Assert.Contains(projectNames, name => name.Equals("DigitalBrain.Abstractions", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(projectNames, name => name.Equals("DigitalBrain.Aspire", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(projectNames, name => name.Equals("DigitalBrain.Aspire.Hosting", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(projectNames, name => name.Equals("DigitalBrain.Client", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(projectNames, name => name.Equals("DigitalBrain.DevTools", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(projectNames, name => name.Equals("DigitalBrain.Kernel", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(projectNames, name => name.Equals("Google", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(projectNames, name => name.Equals("Google.Contracts", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(projectNames, name => name.Equals("Salesforce", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(projectNames, name => name.Equals("Salesforce.Contracts", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(projectNames, name => name.StartsWith("Brain.Client", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(projectNames, name => name.StartsWith("Brain.Contracts", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(projectNames, name => name.Equals("Brain.Kernel", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(projectNames, name => name.Equals("AI", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(projectNames, name => name.Equals("AI.Contracts", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(projectNames, name => name.Equals("Flutter", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(projectNames, name => name.Equals("Flutter.Contracts", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(projectNames, name => name.Equals("Brain.Mcp", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Repository_contains_only_approved_DigitalBrain_projects()
    {
        var repositoryRoot = Path.GetDirectoryName(FindRepositoryFile("Brain.slnx"))!;
        var projectRoots = new[]
        {
            "behaviors",
            "edge",
            "hosts",
            "integrations",
            "kernel",
            "modules",
            "samples",
            "src",
            "tests"
        };
        var projectPaths = projectRoots
            .Select(path => Path.Combine(repositoryRoot, path))
            .Where(Directory.Exists)
            .SelectMany(path => Directory.EnumerateFiles(
                path,
                "*.csproj",
                SearchOption.AllDirectories))
            .Select(path => Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/'))
            .Where(path => !path.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains("/obj/", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "hosts/Brain.Kernel.Host/Brain.Kernel.Host.csproj",
                "hosts/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj",
                "integrations/DigitalBrain.Aspire.Hosting/DigitalBrain.Aspire.Hosting.csproj",
                "integrations/DigitalBrain.Aspire/DigitalBrain.Aspire.csproj",
                "integrations/DigitalBrain.DevTools/DigitalBrain.DevTools.csproj",
                "kernel/DigitalBrain.Abstractions/DigitalBrain.Abstractions.csproj",
                "kernel/DigitalBrain.Client/DigitalBrain.Client.csproj",
                "kernel/DigitalBrain.Kernel/DigitalBrain.Kernel.csproj",
                "modules/Google.Contracts/Google.Contracts.csproj",
                "modules/Google/Google.csproj",
                "modules/Salesforce.Contracts/Salesforce.Contracts.csproj",
                "modules/Salesforce/Salesforce.csproj",
                "samples/DigitalBrain.Quickstart/DigitalBrain.Quickstart.AppHost/DigitalBrain.Quickstart.AppHost.csproj",
                "samples/DigitalBrain.Quickstart/DigitalBrain.Quickstart.Console/DigitalBrain.Quickstart.Console.csproj",
                "samples/DigitalBrain.Quickstart/DigitalBrain.Quickstart.DevUI/DigitalBrain.Quickstart.DevUI.csproj",
                "samples/DigitalBrain.Quickstart/DigitalBrain.Quickstart.Kernel/DigitalBrain.Quickstart.Kernel.csproj",
                "samples/DigitalBrain.Quickstart/DigitalBrain.Quickstart.OrleansDashboard/DigitalBrain.Quickstart.OrleansDashboard.csproj",
                "samples/DigitalBrain.Quickstart/DigitalBrain.Quickstart.TestProvider/DigitalBrain.Quickstart.TestProvider.csproj",
                "tests/Brain.FeasibilityTests/Brain.FeasibilityTests.csproj",
                "tests/DigitalBrain.PackageTests/DigitalBrain.PackageTests.csproj",
                "tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj"
            ],
            projectPaths);
    }

    [Fact]
    public void Superseded_architecture_source_trees_are_empty()
    {
        var repositoryRoot = Path.GetDirectoryName(FindRepositoryFile("Brain.slnx"))!;
        var forbiddenRoots = new[]
        {
            "behaviors",
            "edge",
            "src",
            "modules/AI",
            "modules/AI.Contracts",
            "modules/Brain.Modules.Ai",
            "modules/Brain.Modules.Behaviors",
            "modules/Brain.Modules.Connections",
            "modules/Brain.Modules.Google",
            "modules/Brain.Modules.Salesforce",
            "modules/Brain.Modules.Sdk",
            "modules/Brain.Modules.Web",
            "modules/Brain.Modules.Workspace",
            "modules/Flutter",
            "modules/Flutter.Contracts",
            "tests/Brain.FeasibilityTests/AgentFramework",
            "tests/Brain.FeasibilityTests/TypedReferences",
            "tests/Brain.Tests"
        };
        var remainingFiles = forbiddenRoots
            .Select(path => Path.Combine(repositoryRoot, path))
            .Where(Directory.Exists)
            .SelectMany(path => Directory.EnumerateFiles(
                path,
                "*",
                SearchOption.AllDirectories))
            .Select(path => Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/'))
            .Where(path => !path.Contains("/bin/", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains("/obj/", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(remainingFiles);
    }

    [Fact]
    public void Approved_projects_reference_only_approved_projects()
    {
        var repositoryRoot = Path.GetDirectoryName(FindRepositoryFile("Brain.slnx"))!;
        var pathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var projectRoots = new[]
        {
            "hosts",
            "integrations",
            "kernel",
            "modules",
            "samples",
            "tests"
        };
        var approvedProjects = projectRoots
            .Select(path => Path.Combine(repositoryRoot, path))
            .Where(Directory.Exists)
            .SelectMany(path => Directory.EnumerateFiles(
                path,
                "*.csproj",
                SearchOption.AllDirectories))
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFullPath)
            .ToHashSet(pathComparer);
        var invalidReferences = approvedProjects
            .SelectMany(projectPath => XDocument.Load(projectPath)
                .Descendants("ProjectReference")
                .Select(reference => new
                {
                    ProjectPath = projectPath,
                    Include = reference.Attribute("Include")?.Value
                }))
            .Where(reference => !string.IsNullOrWhiteSpace(reference.Include))
            .Select(reference => new
            {
                reference.ProjectPath,
                ReferencedPath = Path.GetFullPath(
                    reference.Include!
                        .Replace('\\', Path.DirectorySeparatorChar)
                        .Replace('/', Path.DirectorySeparatorChar),
                    Path.GetDirectoryName(reference.ProjectPath)!)
            })
            .Where(reference => !approvedProjects.Contains(reference.ReferencedPath))
            .Select(reference =>
                $"{Path.GetRelativePath(repositoryRoot, reference.ProjectPath)} -> " +
                Path.GetRelativePath(repositoryRoot, reference.ReferencedPath))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(invalidReferences);
    }

    [Fact]
    public void Approved_projects_do_not_compile_or_import_sources_tree()
    {
        var repositoryRoot = Path.GetDirectoryName(FindRepositoryFile("Brain.slnx"))!;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var sourcesRoot = Path.GetFullPath(Path.Combine(repositoryRoot, "sources"));
        var projectRoots = new[]
        {
            "hosts",
            "integrations",
            "kernel",
            "modules",
            "samples",
            "tests"
        };
        var activeBuildFiles = projectRoots
            .Select(path => Path.Combine(repositoryRoot, path))
            .Where(Directory.Exists)
            .SelectMany(path => Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(repositoryRoot, "*", SearchOption.TopDirectoryOnly))
            .Where(path =>
                Path.GetExtension(path).Equals(".csproj", StringComparison.OrdinalIgnoreCase) ||
                Path.GetExtension(path).Equals(".props", StringComparison.OrdinalIgnoreCase) ||
                Path.GetExtension(path).Equals(".targets", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
            .Distinct(OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal);
        var invalidReferences = activeBuildFiles
            .SelectMany(buildFilePath => XDocument.Load(buildFilePath)
                .Descendants()
                .Where(element =>
                    element.Name.LocalName.Equals("Compile", StringComparison.Ordinal) ||
                    element.Name.LocalName.Equals("Import", StringComparison.Ordinal))
                .Select(element => new
                {
                    BuildFilePath = buildFilePath,
                    ReferencedPath = element.Attribute(
                        element.Name.LocalName.Equals("Import", StringComparison.Ordinal)
                            ? "Project"
                            : "Include")?.Value
                }))
            .Where(reference => !string.IsNullOrWhiteSpace(reference.ReferencedPath))
            .Where(reference => ReferencesSourcesTree(
                reference.BuildFilePath,
                reference.ReferencedPath!,
                sourcesRoot,
                comparison))
            .Select(reference =>
                $"{Path.GetRelativePath(repositoryRoot, reference.BuildFilePath)} -> " +
                reference.ReferencedPath)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(invalidReferences);
    }

    [Theory]
    [InlineData("../../sources/shared.cs")]
    [InlineData("$(RepositoryRoot)/sources/shared.cs")]
    [InlineData("$(SourcesRoot)/shared.cs")]
    public void Sources_tree_reference_guard_handles_relative_and_property_paths(string referencedPath)
    {
        var repositoryRoot = Path.GetDirectoryName(FindRepositoryFile("Brain.slnx"))!;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        Assert.True(ReferencesSourcesTree(
            Path.Combine(repositoryRoot, "kernel", "DigitalBrain.Kernel", "DigitalBrain.Kernel.csproj"),
            referencedPath,
            Path.Combine(repositoryRoot, "sources"),
            comparison));
    }

    [Fact]
    public void Root_central_package_versions_match_active_solution_references()
    {
        var solutionPath = FindRepositoryFile("Brain.slnx");
        var repositoryRoot = Path.GetDirectoryName(solutionPath)!;
        var solution = XDocument.Load(solutionPath);
        var referencedPackages = solution
            .Descendants("Project")
            .Select(element => element.Attribute("Path")?.Value)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path!, repositoryRoot))
            .SelectMany(projectPath => XDocument.Load(projectPath)
                .Descendants("PackageReference"))
            .Select(reference => reference.Attribute("Include")?.Value)
            .Where(package => !string.IsNullOrWhiteSpace(package))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var centralVersions = XDocument.Load(
                Path.Combine(repositoryRoot, "Directory.Packages.props"))
            .Descendants("PackageVersion")
            .Select(version => version.Attribute("Include")?.Value)
            .Where(package => !string.IsNullOrWhiteSpace(package))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(referencedPackages, centralVersions);
    }

    [Fact]
    public void Kernel_host_defaults_register_health_checks_and_map_default_endpoints()
    {
        var builder = WebApplication.CreateBuilder();
        builder.AddServiceDefaults();
        var app = builder.Build();

        app.MapDefaultEndpoints();

        Assert.NotNull(app.Services.GetRequiredService<HealthCheckService>());
        var routes = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .ToArray();
        Assert.Contains("/health", routes);
        Assert.Contains("/alive", routes);
    }

    private static string FindRepositoryFile(string fileName)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, fileName);
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException($"Could not locate {fileName} from the test output directory.");
    }

    private static bool ReferencesSourcesTree(
        string projectPath,
        string referencedPath,
        string sourcesRoot,
        StringComparison comparison)
    {
        var normalizedPath = referencedPath.Replace('\\', '/');
        var segments = normalizedPath.Split(
            '/',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Any(segment => segment.Equals("sources", comparison)))
            return true;
        if (referencedPath.Contains("$(", StringComparison.Ordinal))
            return true;

        var fullReferencedPath = Path.GetFullPath(
            normalizedPath.Replace('/', Path.DirectorySeparatorChar),
            Path.GetDirectoryName(projectPath)!);
        return fullReferencedPath.Equals(sourcesRoot, comparison) ||
            fullReferencedPath.StartsWith(
                sourcesRoot + Path.DirectorySeparatorChar,
                comparison);
    }
}
