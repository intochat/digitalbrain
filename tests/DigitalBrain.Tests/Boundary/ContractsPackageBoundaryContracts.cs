using DigitalBrain.AI;
using DigitalBrain.Google;
using DigitalBrain.Kernel;
using DigitalBrain.Salesforce;
using DigitalBrain.Tasks;
using DigitalBrain.Tests.Packages;
using Xunit;

namespace DigitalBrain.Tests.Boundary;

public sealed class ContractsPackageBoundaryContracts
{
    private static readonly string Kernel = PackageOf(typeof(Neuron));
    private static readonly string AiRuntime = PackageOf(typeof(AIModule));
    private static readonly string TasksRuntime = PackageOf(typeof(TasksModule));
    private static readonly string TasksContracts = PackageOf(typeof(ITask));
    private static readonly string GoogleRuntime = PackageOf(typeof(GoogleModule));
    private static readonly string SalesforceRuntime = PackageOf(typeof(SalesforceModule));

    public static TheoryData<string> ConsumerPathPackages { get; } =
        [.. PackageBoundarySupport.ConsumerPath];

    public static TheoryData<string> ContractsPackages { get; } =
        [.. PackageBoundarySupport.ContractsPackages];

    public static TheoryData<string> McpProviderRuntimePackages { get; } =
        [.. PackageBoundarySupport.McpProviderRuntimePackages];

    [Theory]
    [MemberData(nameof(ConsumerPathPackages))]
    public void NothingOnTheConsumerPathCanReachAProviderSdk(string package)
    {
        var reachable = PackageBoundarySupport.PackagesReachableFrom(package)
            .Where(dependency => PackageBoundarySupport.ProviderSdkPrefixes.Any(sdk =>
                dependency.StartsWith(sdk, StringComparison.Ordinal)))
            .ToList();

        Assert.Empty(reachable);
    }

    [Theory]
    [MemberData(nameof(ConsumerPathPackages))]
    public void NothingOnTheConsumerPathCanReachMaf(string package)
    {
        Assert.DoesNotContain(
            PackageBoundarySupport.PackagesReachableFrom(package),
            dependency => dependency.StartsWith("Microsoft.Agents.AI", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(ConsumerPathPackages))]
    public void NothingOnTheConsumerPathCanReachTesting(string package)
    {
        Assert.DoesNotContain(
            PackageInventory.Testing,
            PackageBoundarySupport.ProjectsReachableFrom(package),
            StringComparer.Ordinal);
    }

    [Theory]
    [MemberData(nameof(ConsumerPathPackages))]
    public void NothingOnTheConsumerPathCanReachDartOrFlutterSdkPackages(string package)
    {
        Assert.DoesNotContain(
            PackageBoundarySupport.PackagesReachableFrom(package),
            PackageBoundarySupport.IsDartOrFlutterSdkPackage);
    }

    [Theory]
    [MemberData(nameof(ContractsPackages))]
    public void ContractsPackagesAreFreeOfKernelAndDartFlutterSdks(string package)
    {
        Assert.DoesNotContain(
            Kernel,
            PackageBoundarySupport.ProjectsReachableFrom(package),
            StringComparer.Ordinal);
        Assert.DoesNotContain(
            PackageBoundarySupport.PackagesReachableFrom(package),
            PackageBoundarySupport.IsDartOrFlutterSdkPackage);
    }

    [Fact]
    public void NoProductionTreeReferencesDigitalBrainTesting()
    {
        var offenders = RepositoryLayout.ProjectTreeRoots
            .SelectMany(root => Directory.EnumerateFiles(
                Path.Combine(RepositoryLayout.Root, root),
                "*.csproj",
                SearchOption.AllDirectories))
            .Where(project => !RepositoryLayout.IsIgnoredLookupPath(project))
            .Select(project => Path.GetFileNameWithoutExtension(project)!)
            .Where(name => !string.Equals(name, PackageInventory.Testing, StringComparison.Ordinal))
            .Where(name => PackageBoundarySupport
                .DirectProjectReferencesOf(name)
                .Contains(PackageInventory.Testing, StringComparer.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Empty(offenders);
    }

    [Theory]
    [MemberData(nameof(McpProviderRuntimePackages))]
    public void McpProvidersDependOnSharedMechanics(string package)
    {
        Assert.Contains(
            PackageInventory.IntegrationsMcp,
            PackageBoundarySupport.DirectProjectReferencesOf(package));
        Assert.DoesNotContain(
            PackageBoundarySupport.DirectPackageReferencesOf(package),
            dependency => dependency is "ModelContextProtocol.Core"
                or "Microsoft.AspNetCore.DataProtection"
                or "Microsoft.Extensions.Http");
    }

    [Fact]
    public void AiRuntimeUsesSharedSecurity()
    {
        Assert.Contains(
            PackageInventory.Security,
            PackageBoundarySupport.DirectProjectReferencesOf(AiRuntime));
        Assert.DoesNotContain(
            "Microsoft.AspNetCore.DataProtection",
            PackageBoundarySupport.DirectPackageReferencesOf(AiRuntime));
    }

    [Fact]
    public void TasksRemainIndependentFromAiAndProviders()
    {
        Assert.Equal(
            new[] { Kernel, TasksContracts }.Order(StringComparer.Ordinal),
            PackageBoundarySupport.DirectCompileProjectReferencesOf(TasksRuntime)
                .Order(StringComparer.Ordinal));

        var projects = PackageBoundarySupport.CompileProjectsReachableFrom(TasksRuntime);
        Assert.DoesNotContain(
            projects,
            project => project.StartsWith(AiRuntime, StringComparison.Ordinal)
                || project.StartsWith(GoogleRuntime, StringComparison.Ordinal)
                || project.StartsWith(SalesforceRuntime, StringComparison.Ordinal)
                || project.StartsWith(PackageInventory.IntegrationsMcp, StringComparison.Ordinal));
    }

    private static string PackageOf(Type type)
        => type.Assembly.GetName().Name
           ?? throw new InvalidOperationException($"Assembly for {type.FullName} has no name.");
}
