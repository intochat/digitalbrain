using System.Diagnostics;
using System.Text.Json;
using DigitalBrain.Tests.Boundary;

namespace DigitalBrain.Tests.Packages;

internal static class PackageInventory
{
    private static readonly Lazy<EvaluatedRepositoryGraph> RepositoryGraph =
        new(EvaluatedRepositoryGraph.Create);

    internal const string Metapackage = "DigitalBrain";
    internal const string Abstractions = "DigitalBrain.Abstractions";
    internal const string Behaviors = "DigitalBrain.Behaviors";
    internal const string BehaviorsRuntime = "DigitalBrain.Behaviors.Runtime";
    internal const string Client = "DigitalBrain.Client";
    internal const string Kernel = "DigitalBrain.Kernel";
    internal const string Security = "DigitalBrain.Security";
    internal const string Testing = "DigitalBrain.Testing";
    internal const string Aspire = "DigitalBrain.Aspire";
    internal const string AspireHosting = "DigitalBrain.Aspire.Hosting";
    internal const string IntegrationsMcp = "DigitalBrain.Integrations.Mcp";
    internal const string IntegrationsMcpAspireHosting = "DigitalBrain.Integrations.Mcp.Aspire.Hosting";
    internal const string Ui = "DigitalBrain.Ui";
    internal const string ProductSiloHost = "DigitalBrain.Host";
    internal const string ProductAppHost = "DigitalBrain.AppHost";
    internal const string AccountEnrichment = "DigitalBrain.AccountEnrichment";
    internal const string Quickstart = "DigitalBrain.Quickstart";
    internal const string QuickstartContracts = "DigitalBrain.Quickstart.Contracts";

    internal const string ModulesAi = "DigitalBrain.Modules.AI";
    internal const string ModulesAiContracts = "DigitalBrain.Modules.AI.Contracts";
    internal const string ModulesAiAspireHosting = "DigitalBrain.Modules.AI.Aspire.Hosting";
    internal const string ModulesGoogle = "DigitalBrain.Modules.Google";
    internal const string ModulesGoogleContracts = "DigitalBrain.Modules.Google.Contracts";
    internal const string ModulesGoogleAspireHosting = "DigitalBrain.Modules.Google.Aspire.Hosting";
    internal const string ModulesSalesforce = "DigitalBrain.Modules.Salesforce";
    internal const string ModulesSalesforceContracts = "DigitalBrain.Modules.Salesforce.Contracts";
    internal const string ModulesSalesforceAspireHosting = "DigitalBrain.Modules.Salesforce.Aspire.Hosting";
    internal const string ModulesTasks = "DigitalBrain.Modules.Tasks";
    internal const string ModulesTasksContracts = "DigitalBrain.Modules.Tasks.Contracts";
    internal const string ModulesTasksAspireHosting = "DigitalBrain.Modules.Tasks.Aspire.Hosting";
    internal const string ModulesTime = "DigitalBrain.Modules.Time";
    internal const string ModulesTimeContracts = "DigitalBrain.Modules.Time.Contracts";
    internal const string ModulesTimeAspireHosting = "DigitalBrain.Modules.Time.Aspire.Hosting";
    internal const string ModulesFlutter = "DigitalBrain.Modules.Flutter";
    internal const string ModulesFlutterContracts = "DigitalBrain.Modules.Flutter.Contracts";
    internal const string ModulesFlutterAspireHosting = "DigitalBrain.Modules.Flutter.Aspire.Hosting";

    internal const string ModulesPrefix = "DigitalBrain.Modules.";
    internal const string IntegrationsPrefix = "DigitalBrain.Integrations.";
    internal const string AspireFamilyPrefix = "DigitalBrain.Aspire";
    internal const string UiPrefix = "DigitalBrain.Ui.";

    internal static readonly string[] Packable =
    [
        Metapackage,
        Abstractions,
        Behaviors,
        Kernel,
        Client,
        Testing,
        Aspire,
        AspireHosting,
        Security,
        IntegrationsMcp,
        IntegrationsMcpAspireHosting,
        ModulesAiContracts,
        ModulesAi,
        ModulesAiAspireHosting,
        ModulesGoogleContracts,
        ModulesGoogle,
        ModulesGoogleAspireHosting,
        ModulesSalesforceContracts,
        ModulesSalesforce,
        ModulesSalesforceAspireHosting,
        ModulesTasksContracts,
        ModulesTasks,
        ModulesTimeContracts,
        ModulesTime,
        ModulesFlutterContracts,
        ModulesFlutter,
        ModulesFlutterAspireHosting,
        QuickstartContracts,
        Quickstart,
    ];

    internal static readonly string[] ProductRuntimeModules =
    [
        ModulesAi,
        ModulesFlutter,
        ModulesGoogle,
        ModulesSalesforce,
    ];

    internal static readonly string[] ProductHostingModules =
    [
        ModulesAiAspireHosting,
        ModulesFlutterAspireHosting,
        ModulesGoogleAspireHosting,
        ModulesSalesforceAspireHosting,
    ];

    internal static readonly string[] AbstractionsDirectPackages = ["Microsoft.Orleans.Sdk"];

    internal static readonly string[] ClientDirectProjects = [Abstractions];

    internal static readonly string[] ClientDirectPackages = ["Microsoft.Orleans.Client"];

    internal static readonly string[] SecurityDirectPackages = [];

    internal static readonly string[] IntegrationsMcpDirectProjects = [Security];

    internal static readonly string[] IntegrationsMcpDirectPackages =
    [
        "Microsoft.Extensions.Http",
        "Microsoft.Orleans.Journaling",
        "ModelContextProtocol.Core",
    ];

    internal static readonly string[] IntegrationsMcpAspireHostingDirectProjects = [AspireHosting];

    internal static readonly string[] IntegrationsMcpAspireHostingCompileReachable =
    [
        Abstractions,
        AspireHosting,
    ];

    internal static readonly string[] MetapackageDirectProjects =
    [
        Abstractions,
        Aspire,
        Client,
    ];

    internal static readonly string[] AspireDirectProjects = [Client];

    internal static readonly string[] AspireDirectPackages = ["Microsoft.Orleans.Client"];

    internal static readonly string[] AspireCompileReachable =
    [
        Abstractions,
        Client,
    ];

    internal static readonly string[] AspireHostingDirectProjects = [Abstractions];

    internal static readonly string[] AspireHostingDirectPackages =
    [
        "Aspire.Hosting",
        "Aspire.Hosting.Azure.Storage",
        "Aspire.Hosting.Orleans",
    ];

    internal static readonly string[] TestingDirectProjects =
    [
        Client,
        IntegrationsMcp,
        Kernel,
    ];

    internal static readonly string[] TestingDirectPackages =
    [
        "Aspire.Hosting.Testing",
        "Microsoft.Orleans.TestingHost",
        "xunit.v3.extensibility.core",
    ];

    internal static readonly string[] TestingCompileReachable =
    [
        Abstractions,
        Client,
        IntegrationsMcp,
        Kernel,
        Security,
    ];

    internal static bool IsModulesProject(string project) =>
        project.StartsWith(ModulesPrefix, StringComparison.Ordinal);

    internal static bool IsModuleRuntime(string project) =>
        IsModulesProject(project)
        && !project.EndsWith(".Contracts", StringComparison.Ordinal)
        && !project.EndsWith(".Aspire.Hosting", StringComparison.Ordinal);

    internal static bool IsModuleHosting(string project) =>
        project.StartsWith(ModulesPrefix, StringComparison.Ordinal)
        && project.EndsWith(".Aspire.Hosting", StringComparison.Ordinal);

    internal static bool IsIntegrationsProject(string project) =>
        project.StartsWith(IntegrationsPrefix, StringComparison.Ordinal);

    internal static bool IsAspireFamilyProject(string project) =>
        project.StartsWith(AspireFamilyPrefix, StringComparison.Ordinal);

    internal static bool IsUiFamilyProject(string project) =>
        project is Ui || project.StartsWith(UiPrefix, StringComparison.Ordinal);

    internal static bool IsForbiddenOnConsumerResidual(string project) =>
        project is Kernel or Security or Testing or AspireHosting
        || IsIntegrationsProject(project)
        || IsModulesProject(project);

    internal static bool IsForbiddenOnAspireHostingProject(string project) =>
        project is Kernel or Client or Aspire or Security or Testing
        || IsModulesProject(project)
        || IsIntegrationsProject(project)
        || IsUiFamilyProject(project);

    internal static bool IsForbiddenOnIntegrationsMcpProject(string project) =>
        project is Kernel or Client or Testing
        || IsModulesProject(project)
        || IsAspireFamilyProject(project);

    internal static bool IsForbiddenOnIntegrationsMcpPackage(string package) =>
        package is "ModelContextProtocol"
            or "ModelContextProtocol.AspNetCore"
            or "Microsoft.AspNetCore.DataProtection"
        || package.StartsWith("OpenAI", StringComparison.Ordinal)
        || package.StartsWith("OllamaSharp", StringComparison.Ordinal)
        || package.StartsWith("Microsoft.Agents.AI", StringComparison.Ordinal);

    internal static bool IsForbiddenOnTestingProject(string project) =>
        IsModulesProject(project)
        || IsAspireFamilyProject(project)
        || IsUiFamilyProject(project);

    internal static EvaluatedRepositoryGraph EvaluateRepositoryGraph() =>
        RepositoryGraph.Value;
}

internal sealed class EvaluatedRepositoryGraph
{
    private const string ProjectReferenceItem = "ProjectReference";
    private const string PackageReferenceItem = "PackageReference";
    private const string PackageVersionItem = "PackageVersion";
    private const string IsImplicitlyDefinedMetadata = "IsImplicitlyDefined";
    private const string IsPackableProperty = "IsPackable";

    private readonly Dictionary<string, string[]> _directProjectReferences;

    private EvaluatedRepositoryGraph(
        Dictionary<string, string[]> directProjectReferences,
        HashSet<string> projectNames,
        HashSet<string> packableProjectNames,
        int packageReferenceCount,
        HashSet<string> packageIds,
        int centralPackageVersionCount)
    {
        _directProjectReferences = directProjectReferences;
        ProjectNames = projectNames;
        PackableProjectNames = packableProjectNames;
        PackageReferenceCount = packageReferenceCount;
        PackageIdCount = packageIds.Count;
        CentralPackageVersionCount = centralPackageVersionCount;
    }

    internal int ProjectCount => ProjectNames.Count;

    internal int ProjectReferenceCount => _directProjectReferences.Values.Sum(references => references.Length);

    internal int PackageReferenceCount { get; }

    internal int PackageIdCount { get; }

    internal int CentralPackageVersionCount { get; }

    internal HashSet<string> ProjectNames { get; }

    internal HashSet<string> PackableProjectNames { get; }

    internal IEnumerable<string> DirectProjectReferencesOf(string project) =>
        _directProjectReferences.TryGetValue(project, out var references)
            ? references
            : throw new InvalidOperationException($"No evaluated project named '{project}' exists.");

    internal static EvaluatedRepositoryGraph Create()
    {
        var directProjectReferences = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var projectNames = new HashSet<string>(StringComparer.Ordinal);
        var packableProjectNames = new HashSet<string>(StringComparer.Ordinal);
        var packageIds = new HashSet<string>(StringComparer.Ordinal);
        var packageReferenceCount = 0;

        foreach (var projectFile in RepositoryLayout.ProjectTreeRoots
                     .Append("tests")
                     .SelectMany(root => Directory.EnumerateFiles(
                         Path.Combine(RepositoryLayout.Root, root),
                         "*.csproj",
                         SearchOption.AllDirectories))
                     .Where(file => !RepositoryLayout.IsIgnoredLookupPath(file)))
        {
            var project = Path.GetFileNameWithoutExtension(projectFile);
            using var evaluation = Evaluate(projectFile, [ProjectReferenceItem, PackageReferenceItem]);
            projectNames.Add(project);
            directProjectReferences.Add(
                project,
                Items(evaluation.RootElement, ProjectReferenceItem)
                    .Select(item => Path.GetFileNameWithoutExtension(ItemValue(item, "FullPath")))
                    .ToArray());

            if (Property(evaluation.RootElement, IsPackableProperty).Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                packableProjectNames.Add(project);
            }

            foreach (var package in Items(evaluation.RootElement, PackageReferenceItem)
                         .Where(item => !ItemValue(item, IsImplicitlyDefinedMetadata).Equals("true", StringComparison.OrdinalIgnoreCase)))
            {
                packageReferenceCount++;
                packageIds.Add(ItemValue(package, "Identity"));
            }
        }

        using var centralPackages = Evaluate(
            Path.Combine(RepositoryLayout.Root, "Directory.Packages.props"),
            [PackageVersionItem]);
        var centralPackageVersionCount = Items(centralPackages.RootElement, PackageVersionItem).Length;

        return new EvaluatedRepositoryGraph(
            directProjectReferences,
            projectNames,
            packableProjectNames,
            packageReferenceCount,
            packageIds,
            centralPackageVersionCount);
    }

    private static JsonDocument Evaluate(string projectFile, string[] items)
    {
        var startInfo = new ProcessStartInfo(DotNetExecutable())
        {
            WorkingDirectory = RepositoryLayout.Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        startInfo.ArgumentList.Add("msbuild");
        startInfo.ArgumentList.Add(projectFile);
        startInfo.ArgumentList.Add($"-getProperty:{IsPackableProperty}");
        startInfo.ArgumentList.Add($"-getItem:{string.Join(',', items)}");
        startInfo.ArgumentList.Add("-nologo");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The pinned dotnet host did not start.");

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"MSBuild evaluation failed for '{projectFile}': {error}");
        }

        return JsonDocument.Parse(output);
    }

    private static string DotNetExecutable()
    {
        var root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(root))
        {
            var candidate = Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
    }

    private static JsonElement[] Items(JsonElement root, string itemType) =>
        root.TryGetProperty("Items", out var items)
        && items.TryGetProperty(itemType, out var values)
            ? values.EnumerateArray().ToArray()
            : [];

    private static string ItemValue(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) ? value.GetString() ?? string.Empty : string.Empty;

    private static string Property(JsonElement root, string name) =>
        root.TryGetProperty("Properties", out var properties)
        && properties.TryGetProperty(name, out var value)
            ? value.GetString() ?? string.Empty
            : string.Empty;
}
