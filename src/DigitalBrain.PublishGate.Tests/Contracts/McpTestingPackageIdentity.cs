using Xunit;

namespace DigitalBrain.Tests.Contracts;

public sealed class McpTestingPackageIdentity
{
    [Fact(DisplayName =
        "DigitalBrain.Mcp.Testing is not a packable package or solution project — McpTestEdge lives in Integrations.Tests")]
    public void McpTestingPackageIdentityIsAbsorbedIntoIntegrationsTests()
    {
        var mcpTestingProject = RepositoryAssets.Path(
            "src",
            "core",
            "mcp",
            "DigitalBrain.Mcp.Testing",
            "DigitalBrain.Mcp.Testing.csproj");
        Assert.False(
            File.Exists(mcpTestingProject),
            "DigitalBrain.Mcp.Testing.csproj must not exist; package identity is absorbed.");

        var solution = File.ReadAllText(RepositoryAssets.Path("DigitalBrain.slnx"));
        Assert.DoesNotContain("DigitalBrain.Mcp.Testing", solution, StringComparison.Ordinal);

        var packableProjects = Directory
            .EnumerateFiles(RepositoryAssets.Path(), "*.csproj", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains("<IsPackable>true</IsPackable>", StringComparison.Ordinal))
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .ToArray();
        Assert.DoesNotContain("DigitalBrain.Mcp.Testing", packableProjects, StringComparer.Ordinal);

        var edgeSource = RepositoryAssets.Path(
            "src",
            "core",
            "mcp",
            "DigitalBrain.Integrations.Tests",
            "McpTestEdge.cs");
        Assert.True(
            File.Exists(edgeSource),
            "McpTestEdge source must live in DigitalBrain.Integrations.Tests.");

        var edgeText = File.ReadAllText(edgeSource);
        Assert.Contains("namespace DigitalBrain.Mcp.Testing", edgeText, StringComparison.Ordinal);
        Assert.Contains("public static class McpTestEdge", edgeText, StringComparison.Ordinal);
    }
}
