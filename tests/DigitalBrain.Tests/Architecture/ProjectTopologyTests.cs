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
    public void Active_solution_contains_only_the_product_foundation_projects()
    {
        var solutionPath = FindRepositoryFile("Brain.slnx");
        var projectPaths = XDocument.Load(solutionPath)
            .Descendants("Project")
            .Select(element => element.Attribute("Path")?.Value)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .ToArray();
        var projectNames = projectPaths
            .Select(path => Path.GetFileNameWithoutExtension(path)!)
            .ToArray();

        Assert.Equal(
            ["Google", "Google.Contracts"],
            ProductProjects(projectNames, "Google"));
        Assert.Equal(
            ["AI", "AI.Contracts"],
            ProductProjects(projectNames, "AI"));
        Assert.Equal(
            ["Flutter", "Flutter.Contracts"],
            ProductProjects(projectNames, "Flutter"));
        Assert.Equal(
            ["Brain.FeasibilityTests", "DigitalBrain.Tests"],
            projectPaths
                .Where(path => path.StartsWith("tests/", StringComparison.OrdinalIgnoreCase))
                .Select(path => Path.GetFileNameWithoutExtension(path)!)
                .Order(StringComparer.Ordinal)
                .ToArray());

        var forbiddenFragments = new[]
        {
            "Modules.Sdk",
            "Modules.Connections",
            "UiGateway",
            "ServiceDefaults"
        };
        Assert.DoesNotContain(
            projectNames,
            name => forbiddenFragments.Any(
                fragment => name.Contains(fragment, StringComparison.OrdinalIgnoreCase)));

        var supersededProjects = new[]
        {
            "Brain.Modules.Workspace",
            "Brain.Modules.Web",
            "Brain.Modules.Behaviors"
        };
        Assert.DoesNotContain(
            projectNames,
            name => supersededProjects.Contains(name, StringComparer.OrdinalIgnoreCase));
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

    private static string[] ProductProjects(IEnumerable<string> projectNames, string prefix) =>
        projectNames
            .Where(name =>
                name.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .ToArray();

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
}
