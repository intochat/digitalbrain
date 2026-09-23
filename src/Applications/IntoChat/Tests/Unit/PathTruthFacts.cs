using System.Xml.Linq;

namespace IntoChat.Tests;

public sealed class PathTruthFacts
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DigitalBrain.slnx")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName
            ?? throw new InvalidOperationException($"Repository root (DigitalBrain.slnx) was not found above {AppContext.BaseDirectory}.");
    }

    private static string PathInRepo(string relative) => Path.Combine(RepositoryRoot, relative.Replace('/', Path.DirectorySeparatorChar));

    private static string Read(string relative) => File.ReadAllText(PathInRepo(relative));

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
        || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void EveryProjectReferenceResolvesToAnExistingFile()
    {
        var broken = new List<string>();
        foreach (var project in Directory.EnumerateFiles(PathInRepo("src"), "*.csproj", SearchOption.AllDirectories))
        {
            if (IsBuildOutput(project)) { continue; }
            var document = XDocument.Load(project);
            foreach (var reference in document.Descendants("ProjectReference"))
            {
                var include = reference.Attribute("Include")?.Value;
                if (string.IsNullOrWhiteSpace(include)) { continue; }
                var target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project)!, include));
                if (!File.Exists(target))
                {
                    broken.Add($"{Path.GetRelativePath(RepositoryRoot, project)} -> {include}");
                }
            }
        }
        Assert.Empty(broken);
    }

    [Fact]
    public void FlutterContractsAreReferencedAtTheirMovedGooglePath()
    {
        var coding = Read("src/Modules/Coding/Tests/Unit/DigitalBrain.Modules.Coding.Tests.Unit.csproj");
        Assert.Contains("Google/Flutter/Contracts/DigitalBrain.Modules.Flutter.Contracts.csproj", coding);
        Assert.DoesNotContain("Modules/Flutter/Contracts", coding);
    }

    [Fact]
    public void CiAndDeployUseTheMovedFlutterWorkspace()
    {
        foreach (var workflow in new[] { ".github/workflows/ci.yml", ".github/workflows/deploy.yml" })
        {
            var text = Read(workflow);
            Assert.Contains("src/Modules/Google/Flutter", text);
            Assert.DoesNotContain("src/Modules/Flutter", text);
        }
    }

    [Fact]
    public void KernelMcpIsGoneFromEveryPackagingPath()
    {
        string[] files =
        [
            ".github/workflows/deploy.yml",
            "src/Applications/IntoChat/IntoChat/Dockerfile",
            "src/Applications/IntoChat/IntoChat/docker-entrypoint.sh",
            "src/Applications/IntoChat/IntoChat/Properties/PublishProfiles/Container.pubxml",
        ];
        foreach (var file in files)
        {
            var text = Read(file);
            Assert.DoesNotContain("DigitalBrain.Mcp", text);
            Assert.DoesNotContain("src/Modules/DigitalBrain/Mcp", text);
        }
    }

    [Fact]
    public void ContainerModuleListsMatchTheLiveAppHostChain()
    {
        string[] live =
        [
            "DigitalBrain.AI.AIModule, DigitalBrain.Modules.AI",
            "DigitalBrain.Memory.MemoryModule, DigitalBrain.Modules.Memory",
            "DigitalBrain.ClickHouse.ClickHouseModule, DigitalBrain.Modules.ClickHouse",
            "DigitalBrain.Supabase.SupabaseModule, DigitalBrain.Modules.Supabase",
            "DigitalBrain.Time.TimeModule, DigitalBrain.Modules.Time",
            "DigitalBrain.Google.Gmail.GmailModule, DigitalBrain.Modules.Google.Gmail",
            "DigitalBrain.Salesforce.SalesforceModule, DigitalBrain.Modules.Salesforce",
            "DigitalBrain.Microsoft.Aspire.AspireModule, DigitalBrain.Modules.Microsoft.Aspire",
            "DigitalBrain.Microsoft.GitHub.GitHubModule, DigitalBrain.Modules.Microsoft.GitHub",
            "DigitalBrain.Microsoft.Roslyn.RoslynModule, DigitalBrain.Modules.Microsoft.Roslyn",
            "DigitalBrain.Microsoft.DotNet.DotNetModule, DigitalBrain.Modules.Microsoft.DotNet",
            "DigitalBrain.Coding.CodingModule, DigitalBrain.Modules.Coding",
            "DigitalBrain.Behavior.BehaviorModule, DigitalBrain.Modules.Behavior",
            "DigitalBrain.Flutter.FlutterModule, DigitalBrain.Modules.Flutter",
        ];
        foreach (var file in new[]
        {
            "src/Applications/IntoChat/IntoChat/Dockerfile",
            "src/Applications/IntoChat/IntoChat/Properties/PublishProfiles/Container.pubxml",
        })
        {
            var text = Read(file);
            foreach (var module in live)
            {
                Assert.Contains(module, text);
            }
            Assert.DoesNotContain("Excel", text);
            Assert.DoesNotContain("DigitalBrain.Google.GoogleModule", text);
            Assert.DoesNotContain("DigitalBrain.Microsoft.MicrosoftModule", text);
        }
    }

    [Fact]
    public void DecisionAndEpicRegistersExist()
    {
        Assert.True(File.Exists(PathInRepo("docs/product/decisions/README.md")), "The ADR register must exist.");
        Assert.True(File.Exists(PathInRepo("docs/product/decisions/0001-process-runner-ownership.md")), "Existing ADR 0001 must stay indexed.");
        Assert.True(File.Exists(PathInRepo("docs/product/epics/README.md")), "The epic index must exist.");
    }
}