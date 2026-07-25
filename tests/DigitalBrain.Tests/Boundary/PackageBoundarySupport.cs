using System.Xml.Linq;

namespace DigitalBrain.Tests.Boundary;

internal static class PackageBoundarySupport
{
    internal static readonly string RepositoryRoot = LocateRepositoryRoot();

    internal static readonly string[] ConsumerPath =
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

    internal static readonly string[] ContractsPackages =
    [
        "DigitalBrain.Modules.AI.Contracts",
        "DigitalBrain.Modules.Google.Contracts",
        "DigitalBrain.Modules.Salesforce.Contracts",
        "DigitalBrain.Modules.Tasks.Contracts",
        "DigitalBrain.Modules.Time.Contracts",
        "DigitalBrain.Modules.Flutter.Contracts",
        "DigitalBrain.Quickstart.Contracts",
    ];

    internal static readonly string[] HostingPackages =
    [
        "DigitalBrain.Aspire.Hosting",
        "DigitalBrain.Modules.AI.Aspire.Hosting",
        "DigitalBrain.Modules.Flutter.Aspire.Hosting",
        "DigitalBrain.Modules.Google.Aspire.Hosting",
        "DigitalBrain.Modules.Salesforce.Aspire.Hosting",
        "DigitalBrain.Integrations.Mcp.Aspire.Hosting",
    ];

    internal static readonly string[] McpProviderRuntimePackages =
    [
        "DigitalBrain.Modules.Google",
        "DigitalBrain.Modules.Salesforce",
    ];

    internal static readonly string[] ProviderSdkPrefixes =
        ["OpenAI", "Microsoft.Extensions.AI.OpenAI", "OllamaSharp", "ModelContextProtocol"];

    internal static readonly string[] ProductionRoots = ["src", "modules", "samples"];

    internal static bool IsDartOrFlutterSdkPackage(string package) =>
        !package.StartsWith("DigitalBrain", StringComparison.Ordinal)
        && (package.Contains("Flutter", StringComparison.OrdinalIgnoreCase)
            || package.StartsWith("Dart", StringComparison.OrdinalIgnoreCase));

    internal static bool IsPackable(string projectFile) =>
        XDocument.Load(projectFile)
            .Descendants("IsPackable")
            .Any(element => string.Equals(element.Value, "true", StringComparison.OrdinalIgnoreCase));

    internal static HashSet<string> PackagesReachableFrom(string package)
    {
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        foreach (var project in ProjectsReachableFrom(package).Append(package))
        {
            reachable.UnionWith(DirectPackageReferencesOf(project));
        }

        return reachable;
    }

    internal static HashSet<string> ProjectsReachableFrom(string package)
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

    internal static HashSet<string> CompileProjectsReachableFrom(string package)
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

    internal static IEnumerable<string> DirectProjectReferencesOf(string package) =>
        ReferenceElements(package, "ProjectReference")
            .Select(reference => Path.GetFileNameWithoutExtension(IncludeOf(reference).Replace('\\', '/')));

    internal static IEnumerable<string> DirectCompileProjectReferencesOf(string package) =>
        ReferenceElements(package, "ProjectReference")
            .Where(CompilesAgainst)
            .Select(reference => Path.GetFileNameWithoutExtension(IncludeOf(reference).Replace('\\', '/')));

    internal static IEnumerable<string> DirectPackageReferencesOf(string package) =>
        ReferenceElements(package, "PackageReference")
            .Where(FlowsToConsumers)
            .Select(IncludeOf);

    internal static bool IsIgnoredLookupPath(string file)
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
