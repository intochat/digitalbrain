using System.Xml.Linq;
using Xunit;

namespace Brain.Aspire.Tests;

public sealed class ProcessCompositionTests
{
    [Fact]
    public void ProductHost_references_client_hosting_but_not_orleans_server()
    {
        var project = LoadProject("srcv2/CoreV2/DigitalBrain.ProductHost/DigitalBrain.ProductHost.csproj");
        var projectReferences = project
            .Descendants("ProjectReference")
            .Select(reference => Path.GetFileNameWithoutExtension((string?)reference.Attribute("Include")))
            .ToArray();
        var packageReferences = project
            .Descendants("PackageReference")
            .Select(reference => (string?)reference.Attribute("Include"))
            .ToArray();

        Assert.Contains("Brain.Aspire", projectReferences);
        Assert.DoesNotContain("Microsoft.Orleans.Server", packageReferences);
        Assert.Equal("Exe", project.Descendants("OutputType").Single().Value);
    }

    private static XDocument LoadProject(string relativePath)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DigitalBrain.slnx")))
            {
                return XDocument.Load(Path.Combine(directory.FullName, relativePath));
            }
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
