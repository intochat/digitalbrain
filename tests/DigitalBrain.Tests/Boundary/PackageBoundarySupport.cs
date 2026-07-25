using System.Xml.Linq;
using DigitalBrain.Tests.Packages;

namespace DigitalBrain.Tests.Boundary;

internal static class PackageBoundarySupport
{
    private const string OpenAiSdkPrefix = "OpenAI";
    private const string MicrosoftOpenAiSdkPrefix = "Microsoft.Extensions.AI.OpenAI";
    private const string OllamaSharpSdkPrefix = "OllamaSharp";
    private const string ModelContextProtocolSdkPrefix = "ModelContextProtocol";

    private const string ProjectReferenceElement = "ProjectReference";
    private const string PackageReferenceElement = "PackageReference";
    private const string IsPackableElement = "IsPackable";
    private const string IncludeAttribute = "Include";
    private const string PrivateAssetsAttribute = "PrivateAssets";
    private const string ReferenceOutputAssemblyAttribute = "ReferenceOutputAssembly";
    private const string PrivateAssetsAll = "all";
    private const string FalseLiteral = "false";
    private const string TrueLiteral = "true";

    internal static readonly string RepositoryRoot = RepositoryLayout.Root;

    internal static readonly string[] ProductionRoots = RepositoryLayout.PackableTreeRoots;

    internal static readonly string[] ContractsPackages =
    [
        PackageInventory.ModulesAiContracts,
        PackageInventory.ModulesGoogleContracts,
        PackageInventory.ModulesSalesforceContracts,
        PackageInventory.ModulesTasksContracts,
        PackageInventory.ModulesTimeContracts,
        PackageInventory.ModulesFlutterContracts,
        PackageInventory.QuickstartContracts,
    ];

    internal static readonly string[] ConsumerPath =
    [
        PackageInventory.Metapackage,
        PackageInventory.Abstractions,
        PackageInventory.Client,
        PackageInventory.Aspire,
        PackageInventory.AspireHosting,
        .. ContractsPackages,
    ];

    internal static readonly string[] HostingPackages =
    [
        PackageInventory.AspireHosting,
        PackageInventory.ModulesAiAspireHosting,
        PackageInventory.ModulesFlutterAspireHosting,
        PackageInventory.ModulesGoogleAspireHosting,
        PackageInventory.ModulesSalesforceAspireHosting,
        PackageInventory.IntegrationsMcpAspireHosting,
    ];

    internal static readonly string[] McpProviderRuntimePackages =
    [
        PackageInventory.ModulesGoogle,
        PackageInventory.ModulesSalesforce,
    ];

    internal static readonly string[] ProviderSdkPrefixes =
    [
        OpenAiSdkPrefix,
        MicrosoftOpenAiSdkPrefix,
        OllamaSharpSdkPrefix,
        ModelContextProtocolSdkPrefix,
    ];

    internal static bool IsDartOrFlutterSdkPackage(string package) =>
        !package.StartsWith(PackageInventory.Metapackage, StringComparison.Ordinal)
        && (package.Contains("Flutter", StringComparison.OrdinalIgnoreCase)
            || package.StartsWith("Dart", StringComparison.OrdinalIgnoreCase));

    internal static bool IsPackable(string projectFile) =>
        XDocument.Load(projectFile)
            .Descendants(IsPackableElement)
            .Any(element => string.Equals(element.Value, TrueLiteral, StringComparison.OrdinalIgnoreCase));

    internal static bool IsIgnoredLookupPath(string file) =>
        RepositoryLayout.IsIgnoredLookupPath(file);

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
        ReferenceElements(package, ProjectReferenceElement)
            .Select(reference => Path.GetFileNameWithoutExtension(IncludeOf(reference).Replace('\\', '/')));

    internal static IEnumerable<string> DirectCompileProjectReferencesOf(string package) =>
        ReferenceElements(package, ProjectReferenceElement)
            .Where(CompilesAgainst)
            .Select(reference => Path.GetFileNameWithoutExtension(IncludeOf(reference).Replace('\\', '/')));

    internal static IEnumerable<string> DirectPackageReferencesOf(string package) =>
        ReferenceElements(package, PackageReferenceElement)
            .Where(FlowsToConsumers)
            .Select(IncludeOf);

    private static IEnumerable<XElement> ReferenceElements(string package, string elementName) =>
        XDocument.Load(ProjectFileOf(package)).Descendants(elementName);

    private static bool FlowsToConsumers(XElement reference) =>
        !string.Equals((string?)reference.Attribute(PrivateAssetsAttribute), PrivateAssetsAll, StringComparison.OrdinalIgnoreCase)
        && CompilesAgainst(reference);

    private static bool CompilesAgainst(XElement reference) =>
        !string.Equals((string?)reference.Attribute(ReferenceOutputAssemblyAttribute), FalseLiteral, StringComparison.OrdinalIgnoreCase);

    private static string IncludeOf(XElement reference) =>
        reference.Attribute(IncludeAttribute)?.Value
        ?? throw new InvalidOperationException($"A {reference.Name.LocalName} element carries no {IncludeAttribute} attribute.");

    private static string ProjectFileOf(string package) =>
        Directory.EnumerateFiles(
                RepositoryLayout.Root,
                RepositoryLayout.ProjectFileName(package),
                SearchOption.AllDirectories)
            .Where(file => !RepositoryLayout.IsIgnoredLookupPath(file))
            .Single();
}
