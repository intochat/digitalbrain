using System.Xml.Linq;
using Xunit;

namespace DigitalBrain.Tests.Boundary;

public sealed class CompositionBoundaryContracts
{
    private static readonly string RepositoryRoot = LocateRepositoryRoot();

    private static readonly string[] AllowedDirectProjectReferences =
    [
        "DigitalBrain.Abstractions",
        "DigitalBrain.Client",
        "DigitalBrain.Modules.AI.Contracts",
        "DigitalBrain.Modules.Flutter.Contracts",
        "DigitalBrain.Modules.Time.Contracts",
    ];

    private static readonly string[] AllowedReachableProjects =
    [
        "DigitalBrain.Abstractions",
        "DigitalBrain.Client",
        "DigitalBrain.Modules.AI.Contracts",
        "DigitalBrain.Modules.Flutter.Contracts",
        "DigitalBrain.Modules.Tasks.Contracts",
        "DigitalBrain.Modules.Time.Contracts",
    ];

    private static readonly string[] RequiredStaticRemovedUsings =
    [
        "System.Net.Http",
    ];

    private static readonly string[] RequiredOrleansUsingsStrippedByTarget =
    [
        "Orleans",
        "Orleans.Hosting",
        "Orleans.Runtime",
    ];

    private static readonly string[] ForbiddenSourceSnippets =
    [
        "DigitalBrain.Kernel",
        "DigitalBrain.Integrations",
        "DigitalBrain.Testing",
        "DigitalBrain.Security",
        "DigitalBrain.Modules.",
        "Orleans",
        "IGrainFactory",
        "IChatClient",
        "IServiceProvider",
        "HttpClient",
        "IFlutter",
        "IBehavior",
        "ModelContextProtocol",
        "Microsoft.Agents",
        "OllamaSharp",
    ];

    [Fact(DisplayName = "pre-rail compositions reference only client + contracts — never Kernel or runtimes")]
    public void PreRailCompositionsNeverReferenceKernelOrModuleRuntimes()
    {
        var projectPath = CompositionsProjectPath();
        Assert.True(File.Exists(projectPath), projectPath);

        var document = XDocument.Load(projectPath);
        var projectReferences = DirectProjectReferences(document);
        var packageReferences = document
            .Descendants("PackageReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(name => name is not null)
            .Select(name => name!)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var isPackable = document
            .Descendants("IsPackable")
            .Select(element => element.Value)
            .FirstOrDefault();
        var staticRemovedUsings = document
            .Descendants("ItemGroup")
            .Where(group => group.Parent is XElement parent && parent.Name.LocalName == "Project")
            .Elements("Using")
            .Select(element => element.Attribute("Remove")?.Value)
            .Where(name => name is not null)
            .Select(name => name!)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var orleansStripTarget = document
            .Descendants("Target")
            .Single(target => target.Attribute("Name")?.Value == "StripOrleansUsingsFromCompositions");
        var targetRemovedUsings = orleansStripTarget
            .Descendants("Using")
            .Select(element => element.Attribute("Remove")?.Value)
            .Where(name => name is not null)
            .Select(name => name!)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var beforeTargets = orleansStripTarget.Attribute("BeforeTargets")?.Value ?? string.Empty;

        Assert.Equal(AllowedDirectProjectReferences, projectReferences);
        Assert.Empty(packageReferences);
        Assert.Equal("false", isPackable, ignoreCase: true);
        Assert.Equal(RequiredStaticRemovedUsings, staticRemovedUsings);
        Assert.Equal(RequiredOrleansUsingsStrippedByTarget, targetRemovedUsings);
        Assert.Contains("GenerateGlobalUsings", beforeTargets, StringComparison.Ordinal);
        Assert.Contains("CoreCompile", beforeTargets, StringComparison.Ordinal);

        foreach (var reference in projectReferences)
        {
            Assert.False(
                IsForbiddenCompositionProject(reference),
                $"Direct ProjectReference '{reference}' is Kernel, a module runtime, Integrations, or hosting.");
        }
    }

    [Fact(DisplayName = "pre-rail compositions transitively reach only client + contracts — never Kernel/runtimes/Integrations")]
    public void PreRailCompositionsTransitivelyReachOnlyClientAndContracts()
    {
        var reachable = ProjectsReachableFrom("DigitalBrain.Compositions")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(AllowedReachableProjects, reachable);
        Assert.DoesNotContain(reachable, IsForbiddenCompositionProject);
    }

    [Fact(DisplayName = "pre-rail composition sources never import Kernel, Orleans, Integrations, or provider SDKs")]
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

        foreach (var sourcePath in sources)
        {
            var text = File.ReadAllText(sourcePath);
            foreach (var snippet in ForbiddenSourceSnippets)
            {
                Assert.DoesNotContain(snippet, text, StringComparison.Ordinal);
            }
        }
    }

    private static string CompositionsProjectPath()
        => Path.Combine(
            RepositoryRoot,
            "samples",
            "DigitalBrain.Compositions",
            "DigitalBrain.Compositions.csproj");

    private static string[] DirectProjectReferences(XDocument document)
        => document
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(path => path is not null)
            .Select(path => Path.GetFileNameWithoutExtension(path!))
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static string[] DirectCompileProjectReferences(XDocument document)
        => document
            .Descendants("ProjectReference")
            .Where(CompilesAgainst)
            .Select(element => element.Attribute("Include")?.Value)
            .Where(path => path is not null)
            .Select(path => Path.GetFileNameWithoutExtension(path!))
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static HashSet<string> ProjectsReachableFrom(string package)
    {
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>([package]);

        while (pending.Count > 0)
        {
            foreach (var reference in DirectCompileProjectReferencesOf(pending.Dequeue()))
            {
                if (reachable.Add(reference))
                {
                    pending.Enqueue(reference);
                }
            }
        }

        return reachable;
    }

    private static string[] DirectCompileProjectReferencesOf(string package)
    {
        var projectFile = Directory.EnumerateFiles(RepositoryRoot, $"{package}.csproj", SearchOption.AllDirectories)
            .Where(file => !IsIgnoredLookupPath(file))
            .Single();
        return DirectCompileProjectReferences(XDocument.Load(projectFile));
    }

    private static bool CompilesAgainst(XElement reference)
        => !string.Equals(
            (string?)reference.Attribute("ReferenceOutputAssembly"),
            "false",
            StringComparison.OrdinalIgnoreCase);

    private static bool IsForbiddenCompositionProject(string project)
        => project.Equals("DigitalBrain.Kernel", StringComparison.Ordinal)
            || project.Equals("DigitalBrain.Testing", StringComparison.Ordinal)
            || project.Equals("DigitalBrain.Security", StringComparison.Ordinal)
            || project.StartsWith("DigitalBrain.Integrations", StringComparison.Ordinal)
            || project.EndsWith(".Aspire.Hosting", StringComparison.Ordinal)
            || project.Contains("AccountEnrichment", StringComparison.Ordinal)
            || (project.StartsWith("DigitalBrain.Modules.", StringComparison.Ordinal)
                && !project.EndsWith(".Contracts", StringComparison.Ordinal));

    private static bool IsBuildOutput(string path)
        => IsIgnoredLookupPath(path);

    private static bool IsIgnoredLookupPath(string file)
    {
        var relative = Path.GetRelativePath(RepositoryRoot, file);
        var segments = relative.Split(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        return segments.Any(segment =>
            segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("obj", StringComparison.OrdinalIgnoreCase)
            || segment.Equals(".worktrees", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("node_modules", StringComparison.OrdinalIgnoreCase));
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
