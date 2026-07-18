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
        Assert.Contains(projectNames, name => name.Equals("DigitalBrain.Aspire.Hosting", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(projectNames, name => name.Equals("DigitalBrain.Client", StringComparison.OrdinalIgnoreCase));
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
}
