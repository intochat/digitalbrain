using System.Text.Json;

namespace DigitalBrain.Architecture.Tests.Unit;

// T-03-25, R4-01: Azure lives in DigitalBrain.Runtime.Orleans.Azure, never in the runtime restore graph.
public sealed class RuntimeHasNoAzureFacts
{
    [Fact]
    public void Runtime_restore_graph_has_no_azure()
    {
        var project = Repository.ProjectNamed("DigitalBrain.Runtime.Orleans");
        Assert.True(File.Exists(project.Path), project.Path);
        var assets = Path.Combine(project.Directory, "obj", "project.assets.json");
        Assert.True(File.Exists(assets), $"{assets} is missing; restore DigitalBrain.Runtime.Orleans first.");

        using var document = JsonDocument.Parse(File.ReadAllText(assets));
        var libraries = document.RootElement.GetProperty("libraries").EnumerateObject().Select(library => library.Name).ToList();

        Assert.Contains(libraries, library => library.StartsWith("Microsoft.Orleans.Server/", StringComparison.OrdinalIgnoreCase));
        var azure = libraries.Where(library => library.StartsWith("Azure.", StringComparison.OrdinalIgnoreCase)
            || (library.StartsWith("Microsoft.Orleans.", StringComparison.OrdinalIgnoreCase)
                && library.Split('/')[0].Contains("AzureStorage", StringComparison.OrdinalIgnoreCase))).ToList();
        Assert.True(azure.Count == 0, string.Join(", ", azure));
    }

    // RT-04: among the shipped (src) projects, only the .Azure package references Azure packages directly.
    [Fact]
    public void Only_the_azure_package_references_azure()
    {
        Assert.True(Repository.Projects.ContainsKey(TopologyAzurePackage), $"{TopologyAzurePackage} is missing from the solution.");
        var referencing = Repository.Projects.Values
            .Where(project => project.Path.Replace(Path.DirectorySeparatorChar, '/').Contains("/src/", StringComparison.Ordinal))
            .Where(project => Repository.PackageReferences(project.Path).Any(IsAzure))
            .Select(project => project.Name)
            .ToList();
        Assert.Equal([TopologyAzurePackage], referencing);
    }

    private const string TopologyAzurePackage = "DigitalBrain.Runtime.Orleans.Azure";

    private static bool IsAzure(string package)
        => package.StartsWith("Azure.", StringComparison.OrdinalIgnoreCase)
            || (package.StartsWith("Microsoft.Orleans.", StringComparison.OrdinalIgnoreCase) && package.Contains("AzureStorage", StringComparison.OrdinalIgnoreCase));
}
