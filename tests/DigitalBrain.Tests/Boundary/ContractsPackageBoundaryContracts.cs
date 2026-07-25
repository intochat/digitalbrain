using Xunit;

namespace DigitalBrain.Tests.Boundary;

public sealed class ContractsPackageBoundaryContracts
{
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
            "DigitalBrain.Testing",
            PackageBoundarySupport.ProjectsReachableFrom(package),
            StringComparer.Ordinal);
    }

    [Theory]
    [MemberData(nameof(ConsumerPathPackages))]
    public void NothingOnTheConsumerPathCanReachDartOrFlutterSdkPackages(string package)
    {
        var offenders = PackageBoundarySupport.PackagesReachableFrom(package)
            .Where(PackageBoundarySupport.IsDartOrFlutterSdkPackage)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Empty(offenders);
    }

    [Theory]
    [MemberData(nameof(ContractsPackages))]
    public void ContractsPackagesAreFreeOfKernelAndDartFlutterSdks(string package)
    {
        Assert.DoesNotContain(
            "DigitalBrain.Kernel",
            PackageBoundarySupport.DirectCompileProjectReferencesOf(package),
            StringComparer.Ordinal);
        Assert.DoesNotContain(
            "DigitalBrain.Kernel",
            PackageBoundarySupport.ProjectsReachableFrom(package),
            StringComparer.Ordinal);
        Assert.Empty(
            PackageBoundarySupport.DirectPackageReferencesOf(package)
                .Where(PackageBoundarySupport.IsDartOrFlutterSdkPackage)
                .Order(StringComparer.Ordinal)
                .ToList());
        Assert.Empty(
            PackageBoundarySupport.PackagesReachableFrom(package)
                .Where(PackageBoundarySupport.IsDartOrFlutterSdkPackage)
                .Order(StringComparer.Ordinal)
                .ToList());
    }

    [Fact]
    public void NoProductionTreeReferencesDigitalBrainTesting()
    {
        string[] roots = ["src", "modules", "hosts", "samples"];
        var offenders = roots
            .SelectMany(root => Directory.EnumerateFiles(
                Path.Combine(PackageBoundarySupport.RepositoryRoot, root),
                "*.csproj",
                SearchOption.AllDirectories))
            .Where(project => !PackageBoundarySupport.IsIgnoredLookupPath(project))
            .Where(project => !string.Equals(
                Path.GetFileNameWithoutExtension(project),
                "DigitalBrain.Testing",
                StringComparison.Ordinal))
            .Where(project => PackageBoundarySupport
                .DirectProjectReferencesOf(Path.GetFileNameWithoutExtension(project)!)
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
        Assert.Contains(
            "DigitalBrain.Integrations.Mcp",
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
            "DigitalBrain.Security",
            PackageBoundarySupport.DirectProjectReferencesOf("DigitalBrain.Modules.AI"));
        Assert.DoesNotContain(
            "Microsoft.AspNetCore.DataProtection",
            PackageBoundarySupport.DirectPackageReferencesOf("DigitalBrain.Modules.AI"));
    }

    [Fact]
    public void TasksRemainIndependentFromAiAndProviders()
    {
        Assert.Equal(
            ["DigitalBrain.Kernel", "DigitalBrain.Modules.Tasks.Contracts"],
            PackageBoundarySupport.DirectCompileProjectReferencesOf("DigitalBrain.Modules.Tasks")
                .Order(StringComparer.Ordinal));

        var projects = PackageBoundarySupport.CompileProjectsReachableFrom("DigitalBrain.Modules.Tasks");
        Assert.DoesNotContain(
            projects,
            project => project.StartsWith("DigitalBrain.Modules.AI", StringComparison.Ordinal)
                || project.StartsWith("DigitalBrain.Modules.Google", StringComparison.Ordinal)
                || project.StartsWith("DigitalBrain.Modules.Salesforce", StringComparison.Ordinal)
                || project.StartsWith("DigitalBrain.Integrations.Mcp", StringComparison.Ordinal));
    }
}
