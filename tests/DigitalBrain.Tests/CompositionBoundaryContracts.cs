using System.Xml.Linq;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class CompositionBoundaryContracts
{
    private static readonly string RepositoryRoot = LocateRepositoryRoot();

    private static readonly string[] AllowedProjectReferences =
    [
        "DigitalBrain.Abstractions",
        "DigitalBrain.Client",
        "DigitalBrain.Modules.AI.Contracts",
        "DigitalBrain.Modules.Flutter.Contracts",
        "DigitalBrain.Modules.Time.Contracts",
    ];

    [Fact(DisplayName = "pre-rail compositions reference only client + contracts — never Kernel or runtimes")]
    public void PreRailCompositionsNeverReferenceKernelOrModuleRuntimes()
    {
        var projectPath = Path.Combine(
            RepositoryRoot,
            "samples",
            "DigitalBrain.Compositions",
            "DigitalBrain.Compositions.csproj");
        Assert.True(File.Exists(projectPath), projectPath);

        var document = XDocument.Load(projectPath);
        var projectReferences = document
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(path => path is not null)
            .Select(path => Path.GetFileNameWithoutExtension(path!))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var packageReferences = document
            .Descendants("PackageReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(name => name is not null)
            .Select(name => name!)
            .ToArray();

        Assert.Equal(AllowedProjectReferences, projectReferences);
        Assert.DoesNotContain(projectReferences, name => name == "DigitalBrain.Kernel");
        Assert.DoesNotContain(
            projectReferences,
            name => name.Contains("Integrations", StringComparison.Ordinal));
        Assert.DoesNotContain(
            packageReferences,
            name => name.Contains("Orleans", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Kernel", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("ModelContextProtocol", StringComparison.Ordinal)
                || name.StartsWith("Microsoft.Agents.AI", StringComparison.Ordinal)
                || name.StartsWith("OllamaSharp", StringComparison.Ordinal)
                || name.StartsWith("OpenAI", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "pre-rail composition sources never import Kernel, Orleans, Integrations, or IChatClient")]
    public void PreRailCompositionSourcesStayOnClientAndContracts()
    {
        var compositionsRoot = Path.Combine(
            RepositoryRoot,
            "samples",
            "DigitalBrain.Compositions");
        Assert.True(Directory.Exists(compositionsRoot), compositionsRoot);

        var sources = Directory.EnumerateFiles(compositionsRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .ToArray();
        Assert.NotEmpty(sources);

        string[] forbiddenSnippets =
        [
            "DigitalBrain.Kernel",
            "DigitalBrain.Integrations",
            "Orleans.",
            "IGrainFactory",
            "IChatClient",
            "IServiceProvider",
            "HttpClient",
            "IFlutter",
            "IBehavior",
        ];

        foreach (var sourcePath in sources)
        {
            var text = File.ReadAllText(sourcePath);
            foreach (var snippet in forbiddenSnippets)
            {
                Assert.DoesNotContain(snippet, text, StringComparison.Ordinal);
            }
        }
    }

    private static bool IsBuildOutput(string path)
        => path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

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
