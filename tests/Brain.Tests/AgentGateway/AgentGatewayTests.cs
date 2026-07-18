using System.Xml.Linq;
using Brain.AgentGateway;
using Xunit;

namespace Brain.Tests.AgentGateway;

public sealed class AgentGatewayTests
{
    [Fact]
    public void AgentGateway_uses_Orleans_client_reference()
    {
        var adapterType = typeof(TypedNeuronAgentAdapter);
        var ctor = adapterType.GetConstructors().Single();
        Assert.Contains(ctor.GetParameters(), parameter => parameter.ParameterType.Name is "IClusterClient");

        var sourceRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Brain.AgentGateway"));
        Assert.True(Directory.Exists(sourceRoot), sourceRoot);

        var sources = Directory.EnumerateFiles(sourceRoot, "*.cs").Select(File.ReadAllText).ToList();
        Assert.Contains(sources, source => source.Contains("IClusterClient", StringComparison.Ordinal));
        Assert.Contains(sources, source => source.Contains("Microsoft.Agents.AI.DevUI", StringComparison.Ordinal));
        Assert.Contains(sources, source => source.Contains("MapDevUI", StringComparison.Ordinal));
    }

    [Fact]
    public void AgentGateway_is_not_referenced_by_production_projects()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var productionProjects = Directory.EnumerateFiles(Path.Combine(repoRoot, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains("Brain.AgentGateway", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.NotEmpty(productionProjects);

        foreach (var projectPath in productionProjects)
        {
            var document = XDocument.Load(projectPath);
            var references = document
                .Descendants("ProjectReference")
                .Select(element => (string?)element.Attribute("Include") ?? string.Empty)
                .ToList();

            Assert.DoesNotContain(
                references,
                reference => reference.Contains("Brain.AgentGateway", StringComparison.OrdinalIgnoreCase));
        }

        var agentProject = XDocument.Load(Path.Combine(repoRoot, "src", "Brain.AgentGateway", "Brain.AgentGateway.csproj"));
        var packageIds = agentProject
            .Descendants("PackageReference")
            .Select(element => (string?)element.Attribute("Include") ?? string.Empty)
            .ToList();
        Assert.Contains(packageIds, id => id.Contains("Microsoft.Agents.AI.DevUI", StringComparison.Ordinal));
    }
}
