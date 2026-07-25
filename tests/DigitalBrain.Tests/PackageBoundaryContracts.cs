using System.Xml.Linq;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class PackageBoundaryContracts
{
    private static readonly string[] ProviderSdkPrefixes =
        ["OpenAI", "Microsoft.Extensions.AI.OpenAI", "OllamaSharp", "ModelContextProtocol"];

    private static readonly string RepositoryRoot = LocateRepositoryRoot();

    private static readonly string[] ConsumerPath =
    [
        "DigitalBrain",
        "DigitalBrain.Abstractions",
        "DigitalBrain.Client",
        "DigitalBrain.Aspire",
        "DigitalBrain.Aspire.Hosting",
        "DigitalBrain.Modules.AI.Contracts",
        "DigitalBrain.Modules.Google.Contracts",
        "DigitalBrain.Modules.Salesforce.Contracts",
        "DigitalBrain.Modules.Tasks.Contracts",
        "DigitalBrain.Modules.Time.Contracts",
        "DigitalBrain.Modules.Flutter.Contracts",
        "DigitalBrain.Quickstart.Contracts",
    ];

    private static readonly string[] ProductionRoots = ["src", "modules", "samples"];

    public static TheoryData<string> ConsumerPathPackages { get; } = [.. ConsumerPath];

    public static TheoryData<string> McpProviderRuntimePackages { get; } =
    [
        "DigitalBrain.Modules.Google",
        "DigitalBrain.Modules.Salesforce",
    ];

    [Theory]
    [MemberData(nameof(ConsumerPathPackages))]
    public void NothingOnTheConsumerPathCanReachAProviderSdk(string package)
    {
        var reachable = PackagesReachableFrom(package)
            .Where(dependency => ProviderSdkPrefixes.Any(sdk =>
                dependency.StartsWith(sdk, StringComparison.Ordinal)))
            .ToList();

        Assert.Empty(reachable);
    }

    [Theory]
    [MemberData(nameof(ConsumerPathPackages))]
    public void NothingOnTheConsumerPathCanReachMaf(string package)
    {
        Assert.DoesNotContain(
            PackagesReachableFrom(package),
            dependency => dependency.StartsWith("Microsoft.Agents.AI", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(ConsumerPathPackages))]
    public void NothingOnTheConsumerPathCanReachTheKernel(string package)
    {
        Assert.DoesNotContain(
            "DigitalBrain.Kernel",
            ProjectsReachableFrom(package),
            StringComparer.Ordinal);
    }

    [Theory]
    [MemberData(nameof(ConsumerPathPackages))]
    public void NothingOnTheConsumerPathCanReachTesting(string package)
    {
        Assert.DoesNotContain(
            "DigitalBrain.Testing",
            ProjectsReachableFrom(package),
            StringComparer.Ordinal);
    }

    [Fact]
    public void NoProductionTreeReferencesDigitalBrainTesting()
    {
        string[] roots = ["src", "modules", "hosts", "samples"];
        var offenders = roots
            .SelectMany(root => Directory.EnumerateFiles(
                Path.Combine(RepositoryRoot, root),
                "*.csproj",
                SearchOption.AllDirectories))
            .Where(project => !IsIgnoredLookupPath(project))
            .Where(project => !string.Equals(
                Path.GetFileNameWithoutExtension(project),
                "DigitalBrain.Testing",
                StringComparison.Ordinal))
            .Where(project => DirectProjectReferencesOf(Path.GetFileNameWithoutExtension(project)!)
                .Contains("DigitalBrain.Testing", StringComparer.Ordinal))
            .Select(Path.GetFileNameWithoutExtension)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Empty(offenders);
    }

    [Theory]
    [MemberData(nameof(McpProviderRuntimePackages))]
    public void McpProvidersDependOnSharedMechanics(string package)
    {
        Assert.Contains("DigitalBrain.Integrations.Mcp", DirectProjectReferencesOf(package));
        Assert.DoesNotContain(
            DirectPackageReferencesOf(package),
            dependency => dependency is "ModelContextProtocol.Core"
                or "Microsoft.AspNetCore.DataProtection"
                or "Microsoft.Extensions.Http");
    }

    [Fact]
    public void AiRuntimeUsesSharedSecurity()
    {
        Assert.Contains("DigitalBrain.Security", DirectProjectReferencesOf("DigitalBrain.Modules.AI"));
        Assert.DoesNotContain(
            "Microsoft.AspNetCore.DataProtection",
            DirectPackageReferencesOf("DigitalBrain.Modules.AI"));
    }

    [Fact]
    public void TasksRemainIndependentFromAiAndProviders()
    {
        Assert.Equal(
            ["DigitalBrain.Kernel", "DigitalBrain.Modules.Tasks.Contracts"],
            DirectCompileProjectReferencesOf("DigitalBrain.Modules.Tasks").Order(StringComparer.Ordinal));

        var projects = CompileProjectsReachableFrom("DigitalBrain.Modules.Tasks");
        Assert.DoesNotContain(
            projects,
            project => project.StartsWith("DigitalBrain.Modules.AI", StringComparison.Ordinal)
                || project.StartsWith("DigitalBrain.Modules.Google", StringComparison.Ordinal)
                || project.StartsWith("DigitalBrain.Modules.Salesforce", StringComparison.Ordinal)
                || project.StartsWith("DigitalBrain.Integrations.Mcp", StringComparison.Ordinal));
    }

    [Fact]
    public void NorthboundMcpHostCannotReachSouthboundProviders()
    {
        Assert.Equal(
            ["DigitalBrain.Aspire", "DigitalBrain.Client", "DigitalBrain.Modules.AI.Contracts"],
            DirectCompileProjectReferencesOf("DigitalBrain.Mcp").Order(StringComparer.Ordinal));

        Assert.DoesNotContain(
            CompileProjectsReachableFrom("DigitalBrain.Mcp"),
            project => project.StartsWith("DigitalBrain.Integrations.Mcp", StringComparison.Ordinal)
                || project.StartsWith("DigitalBrain.Modules.Google", StringComparison.Ordinal)
                || project.StartsWith("DigitalBrain.Modules.Salesforce", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "northbound UI host is client + Flutter contracts only — never Kernel or southbound")]
    public void NorthboundUiHostCannotReachKernelOrSouthboundProviders()
    {
        Assert.Equal(
            [
                "DigitalBrain.Aspire",
                "DigitalBrain.Client",
                "DigitalBrain.Modules.Flutter.Contracts",
            ],
            DirectCompileProjectReferencesOf("DigitalBrain.Ui").Order(StringComparer.Ordinal));

        var reachable = CompileProjectsReachableFrom("DigitalBrain.Ui");
        Assert.DoesNotContain(reachable, project => project == "DigitalBrain.Kernel");
        Assert.DoesNotContain(
            reachable,
            project => project == "DigitalBrain.Modules.Flutter"
                || project.StartsWith("DigitalBrain.Integrations.Mcp", StringComparison.Ordinal)
                || project.StartsWith("DigitalBrain.Modules.Google", StringComparison.Ordinal)
                || project.StartsWith("DigitalBrain.Modules.Salesforce", StringComparison.Ordinal)
                || project.StartsWith("DigitalBrain.Modules.AI", StringComparison.Ordinal));
    }

    [Fact]
    public void PackableProjectsMatchTheDeclaredInventory()
    {
        var actual = ProductionRoots
            .SelectMany(root => Directory.EnumerateFiles(
                Path.Combine(RepositoryRoot, root),
                "*.csproj",
                SearchOption.AllDirectories))
            .Where(path => !IsIgnoredLookupPath(path))
            .Where(IsPackable)
            .Select(Path.GetFileNameWithoutExtension!)
            .ToHashSet(StringComparer.Ordinal);

        Assert.All(PackableProjects.Names, name => Assert.Contains(name, actual));
        Assert.All(
            actual,
            name => Assert.True(
                PackableProjects.Names.Contains(name, StringComparer.Ordinal)
                || name == "DigitalBrain",
                $"Unexpected packable project '{name}'."));
    }

    private static bool IsPackable(string projectFile) =>
        XDocument.Load(projectFile)
            .Descendants("IsPackable")
            .Any(element => string.Equals(element.Value, "true", StringComparison.OrdinalIgnoreCase));

    private static HashSet<string> PackagesReachableFrom(string package)
    {
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        foreach (var project in ProjectsReachableFrom(package).Append(package))
        {
            reachable.UnionWith(DirectPackageReferencesOf(project));
        }

        return reachable;
    }

    private static HashSet<string> ProjectsReachableFrom(string package)
    {
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>([package]);

        while (pending.Count > 0)
        {
            foreach (var reference in DirectProjectReferencesOf(pending.Dequeue()))
            {
                if (reachable.Add(reference))
                {
                    pending.Enqueue(reference);
                }
            }
        }

        return reachable;
    }

    private static HashSet<string> CompileProjectsReachableFrom(string package)
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

    private static IEnumerable<string> DirectProjectReferencesOf(string package) =>
        ReferenceElements(package, "ProjectReference")
            .Select(reference => Path.GetFileNameWithoutExtension(IncludeOf(reference).Replace('\\', '/')));

    private static IEnumerable<string> DirectCompileProjectReferencesOf(string package) =>
        ReferenceElements(package, "ProjectReference")
            .Where(CompilesAgainst)
            .Select(reference => Path.GetFileNameWithoutExtension(IncludeOf(reference).Replace('\\', '/')));

    private static IEnumerable<string> DirectPackageReferencesOf(string package) =>
        ReferenceElements(package, "PackageReference")
            .Where(FlowsToConsumers)
            .Select(IncludeOf);

    private static IEnumerable<XElement> ReferenceElements(string package, string elementName) =>
        XDocument.Load(ProjectFileOf(package)).Descendants(elementName);

    private static bool FlowsToConsumers(XElement reference) =>
        !string.Equals((string?)reference.Attribute("PrivateAssets"), "all", StringComparison.OrdinalIgnoreCase)
        && CompilesAgainst(reference);

    private static bool CompilesAgainst(XElement reference) =>
        !string.Equals((string?)reference.Attribute("ReferenceOutputAssembly"), "false", StringComparison.OrdinalIgnoreCase);

    private static string IncludeOf(XElement reference) =>
        reference.Attribute("Include")?.Value
        ?? throw new InvalidOperationException($"A {reference.Name.LocalName} element carries no Include attribute.");

    private static string ProjectFileOf(string package) =>
        Directory.EnumerateFiles(RepositoryRoot, $"{package}.csproj", SearchOption.AllDirectories)
            .Where(file => !IsIgnoredLookupPath(file))
            .Single();

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
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "DigitalBrain.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("DigitalBrain.slnx was not found above the test assembly.");
    }
}
