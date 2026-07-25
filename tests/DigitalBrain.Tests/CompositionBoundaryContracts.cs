using System.Xml.Linq;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class CompositionBoundaryContracts
{
    private static readonly string RepositoryRoot = LocateRepositoryRoot();

    [Fact(DisplayName = "pre-rail compositions never reference Kernel or module runtimes")]
    public void PreRailCompositionsNeverReferenceKernelOrModuleRuntimes()
    {
        var projectPath = Path.Combine(
            RepositoryRoot,
            "samples",
            "DigitalBrain.Compositions",
            "DigitalBrain.Compositions.csproj");
        Assert.True(File.Exists(projectPath), projectPath);

        var references = XDocument.Load(projectPath)
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(path => path is not null)
            .Select(path => Path.GetFileNameWithoutExtension(path!))
            .ToArray();

        Assert.Contains("DigitalBrain.Client", references);
        Assert.Contains("DigitalBrain.Abstractions", references);
        Assert.Contains("DigitalBrain.Modules.Flutter.Contracts", references);
        Assert.Contains("DigitalBrain.Modules.Time.Contracts", references);
        Assert.Contains("DigitalBrain.Modules.AI.Contracts", references);
        Assert.DoesNotContain(references, name => name == "DigitalBrain.Kernel");
        Assert.DoesNotContain(references, name => name == "DigitalBrain.Modules.Flutter");
        Assert.DoesNotContain(references, name => name == "DigitalBrain.Modules.Time");
        Assert.DoesNotContain(references, name => name == "DigitalBrain.Modules.AI");
        Assert.DoesNotContain(references, name => name.Contains("Integrations", StringComparison.Ordinal));
        Assert.DoesNotContain(references, name => name == "DigitalBrain.AccountEnrichment");
    }

    private static string LocateRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DigitalBrain.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Could not locate DigitalBrain.slnx from the test output directory.");
    }
}
