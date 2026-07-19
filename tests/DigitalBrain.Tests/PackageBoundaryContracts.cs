using System.Xml.Linq;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class PackageBoundaryContracts
{
    private static readonly string[] ProviderSdkPrefixes = ["OpenAI", "Anthropic", "Microsoft.Extensions.AI"];

    private static readonly string RepositoryRoot = LocateRepositoryRoot();

    private static readonly string[] ConsumerPath =
    [
        "DigitalBrain",
        "DigitalBrain.Abstractions",
        "DigitalBrain.Client",
        "DigitalBrain.Aspire",
        "DigitalBrain.Aspire.Hosting",
    ];

    private static readonly string[] NeuronHosting = ["DigitalBrain.Testing", "DigitalBrain.DevTools"];

    public static TheoryData<string> ConsumerPathPackages { get; } = [.. ConsumerPath];

    public static TheoryData<string> PackagesThatMayHostNeurons { get; } = [.. NeuronHosting];

    [Theory]
    [MemberData(nameof(ConsumerPathPackages))]
    public void NothingOnTheConsumerPathCanReachAProviderSdk(string package)
    {
        var reachable = PackagesReachableFrom(package)
            .Where(dependency => ProviderSdkPrefixes.Any(sdk => dependency.StartsWith(sdk, StringComparison.Ordinal)))
            .ToList();

        Assert.Empty(reachable);
    }

    [Theory]
    [MemberData(nameof(ConsumerPathPackages))]
    public void NothingOnTheConsumerPathCanReachTheKernel(string package)
    {
        Assert.DoesNotContain("DigitalBrain.Kernel", ProjectsReachableFrom(package), StringComparer.Ordinal);
    }

    [Theory]
    [MemberData(nameof(ConsumerPathPackages))]
    public void NoProductionPackageReferencesTheTestingPackage(string package)
    {
        Assert.DoesNotContain("DigitalBrain.Testing", ProjectsReachableFrom(package), StringComparer.Ordinal);
    }

    [Theory]
    [MemberData(nameof(PackagesThatMayHostNeurons))]
    public void APackageThatHostsNeuronsStillDeclaresNoProviderSdkItself(string package)
    {
        var declared = DirectPackageReferencesOf(package)
            .Where(dependency => ProviderSdkPrefixes.Any(sdk => dependency.StartsWith(sdk, StringComparison.Ordinal)))
            .ToList();

        Assert.Empty(declared);
    }

    [Fact]
    public void TheGuardsCoverEveryPackableProject()
    {
        var guarded = ConsumerPath
            .Concat(NeuronHosting)
            .Append("DigitalBrain.Kernel")
            .ToHashSet(StringComparer.Ordinal);

        var packable = Directory
            .EnumerateFiles(Path.Combine(RepositoryRoot, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(IsPackable)
            .Select(Path.GetFileNameWithoutExtension)
            .ToList();

        Assert.NotEmpty(packable);
        Assert.DoesNotContain(packable, project => !guarded.Contains(project!));
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
        var reached = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>([package]);

        while (pending.Count > 0)
        {
            foreach (var referenced in DirectProjectReferencesOf(pending.Dequeue()))
            {
                if (reached.Add(referenced))
                {
                    pending.Enqueue(referenced);
                }
            }
        }

        return reached;
    }

    private static IEnumerable<string> DirectProjectReferencesOf(string package) =>
        ReferenceElements(package, "ProjectReference")
            .Where(FlowsToConsumers)
            .Select(reference => Path.GetFileNameWithoutExtension(IncludeOf(reference).Replace('\\', '/')));

    private static IEnumerable<string> DirectPackageReferencesOf(string package) =>
        ReferenceElements(package, "PackageReference")
            .Where(FlowsToConsumers)
            .Select(IncludeOf);

    private static IEnumerable<XElement> ReferenceElements(string package, string elementName) =>
        XDocument.Load(ProjectFileOf(package)).Descendants(elementName);

    private static bool FlowsToConsumers(XElement reference) =>
        !string.Equals((string?)reference.Attribute("PrivateAssets"), "all", StringComparison.OrdinalIgnoreCase)
        && !string.Equals((string?)reference.Attribute("ReferenceOutputAssembly"), "false", StringComparison.OrdinalIgnoreCase);

    private static string IncludeOf(XElement reference) =>
        reference.Attribute("Include")?.Value
        ?? throw new InvalidOperationException($"A {reference.Name.LocalName} element carries no Include attribute.");

    private static string ProjectFileOf(string package) =>
        Path.Combine(RepositoryRoot, "src", package, $"{package}.csproj");

    private static string LocateRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DigitalBrain.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("DigitalBrain.slnx was not found above the test assembly.");
    }
}
