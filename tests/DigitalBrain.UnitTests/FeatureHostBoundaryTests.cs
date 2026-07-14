using System.Xml.Linq;
using Xunit;

namespace DigitalBrain.UnitTests;

public sealed class FeatureHostBoundaryTests
{
    [Fact]
    public void FeatureHost_has_only_contract_hosting_and_orleans_client_dependencies()
    {
        var root = RepositoryRoot();
        var projectPath = Path.Combine(
            root,
            "hosts",
            "DigitalBrain.FeatureHost",
            "DigitalBrain.FeatureHost.csproj");
        var project = XDocument.Load(projectPath);
        var projectReferences = project.Descendants("ProjectReference")
            .Select(element => Path.GetFileNameWithoutExtension((string)element.Attribute("Include")!))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var packages = project.Descendants("PackageReference")
            .Select(element => (string)element.Attribute("Include")!)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "DigitalBrain.Features.Sdk",
                "DigitalBrain.Integrations.Google.Contracts",
                "DigitalBrain.Integrations.Salesforce.Contracts",
                "DigitalBrain.Kernel.Contracts"
            ],
            projectReferences);
        Assert.Equal(
            ["Aspire.Azure.Data.Tables", "Aspire.Azure.Storage.Blobs", "Microsoft.Extensions.Hosting", "Microsoft.Orleans.Client", "Microsoft.Orleans.Clustering.AzureStorage"],
            packages);
        var source = string.Join('\n', Directory.EnumerateFiles(
                Path.GetDirectoryName(projectPath)!,
                "*.cs",
                SearchOption.TopDirectoryOnly)
            .Select(File.ReadAllText));
        Assert.DoesNotContain("DefaultAzureCredential", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Google.Apis", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DeveloperForce", source, StringComparison.Ordinal);
        Assert.Contains("HttpFeatureCapabilityClient", source, StringComparison.Ordinal);
        Assert.DoesNotContain("googleapis.com", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("salesforce.com", source, StringComparison.OrdinalIgnoreCase);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Brain.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
