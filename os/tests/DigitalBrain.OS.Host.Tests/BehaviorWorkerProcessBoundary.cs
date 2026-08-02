using Xunit;

namespace DigitalBrain.HostTests;

// Product packaging Option A (I1a): digitalbrain image entrypoint supervises silo +
// behavior-host as separate child processes (os/DigitalBrain.OS.Host/docker-entrypoint.sh).
// Authored assemblies load only in behavior-host; silo residual in-process executor stays closed.
// This gate pins the process boundary in product composition (AppHost + project refs).
public sealed class BehaviorWorkerProcessBoundary
{
    [Fact(DisplayName =
        "Product AppHost registers silo and behavior-host as distinct project resources with Host executor and WaitFor(silo)")]
    public void ProductAppHostKeepsSiloAndBehaviorHostAsSeparateProcesses()
    {
        var root = FindRepositoryRoot();
        var appHost = File.ReadAllText(Path.Combine(root, "os", "DigitalBrain.OS.AppHost", "AppHost.cs"));
        var surface = File.ReadAllText(
            Path.Combine(root, "os", "DigitalBrain.OS.AppHost", "ProductSurfaceResources.cs"));

        Assert.Contains("public const string Silo = \"silo\";", surface, StringComparison.Ordinal);
        Assert.Contains(
            "public const string BehaviorHost = \"behavior-host\";",
            surface,
            StringComparison.Ordinal);

        Assert.Contains(
            "AddProject<Projects.DigitalBrain_OS_Host>(ProductSurfaceResources.Silo)",
            appHost,
            StringComparison.Ordinal);
        Assert.Contains(
            "AddProject<Projects.DigitalBrain_OS_BehaviorHost>(ProductSurfaceResources.BehaviorHost)",
            appHost,
            StringComparison.Ordinal);
        Assert.Contains("BehaviorsModule.HostExecutorName", appHost, StringComparison.Ordinal);
        Assert.Contains(".WaitFor(silo)", appHost, StringComparison.Ordinal);
        Assert.Contains(
            "BehaviorsModule.HostBaseAddressConfigurationKey",
            appHost,
            StringComparison.Ordinal);
        Assert.Contains(
            "BehaviorHostHosting.BrokerBaseAddressConfigurationKey",
            appHost,
            StringComparison.Ordinal);

        // Same process boundary names on the L2 testing composition.
        Assert.Equal("silo", TestingAppHostFixture.SiloResourceName);
        Assert.Equal("behavior-host", TestingAppHostFixture.BehaviorHostResourceName);
    }

    [Fact(DisplayName =
        "Silo project must not reference Behaviors.Host; BehaviorHost alone owns the authored program loader")]
    public void SiloProjectDoesNotReferenceAuthoredAssemblyLoader()
    {
        var root = FindRepositoryRoot();
        var siloProject = File.ReadAllText(
            Path.Combine(root, "os", "DigitalBrain.OS.Host", "DigitalBrain.OS.Host.csproj"));
        var behaviorHostProject = File.ReadAllText(
            Path.Combine(
                root,
                "os",
                "DigitalBrain.OS.BehaviorHost",
                "DigitalBrain.OS.BehaviorHost.csproj"));
        var siloProgram = File.ReadAllText(
            Path.Combine(root, "os", "DigitalBrain.OS.Host", "Program.cs"));
        var behaviorHostProgram = File.ReadAllText(
            Path.Combine(root, "os", "DigitalBrain.OS.BehaviorHost", "Program.cs"));

        Assert.DoesNotContain("DigitalBrain.Behaviors.Host", siloProject, StringComparison.Ordinal);
        Assert.Contains("DigitalBrain.Behaviors.Host", behaviorHostProject, StringComparison.Ordinal);

        Assert.DoesNotContain("AddBehaviorHostEngine", siloProgram, StringComparison.Ordinal);
        Assert.DoesNotContain("BehaviorProgramLoader", siloProgram, StringComparison.Ordinal);
        Assert.Contains("AddBehaviorHostEngine", behaviorHostProgram, StringComparison.Ordinal);
    }

    [Fact(DisplayName =
        "TestingAppHost mirrors product Option A: separate silo and behavior-host project processes")]
    public void TestingAppHostMirrorsSeparateWorkerProcessBoundary()
    {
        var root = FindRepositoryRoot();
        var testingAppHost = File.ReadAllText(
            Path.Combine(
                root,
                "tests",
                "fixtures",
                "apphosts",
                "DigitalBrain.TestingAppHost",
                "AppHost.cs"));

        Assert.Contains(
            "AddProject<Projects.DigitalBrain_OS_Host>(Silo)",
            testingAppHost,
            StringComparison.Ordinal);
        Assert.Contains(
            "AddProject<Projects.DigitalBrain_OS_BehaviorHost>(BehaviorHost)",
            testingAppHost,
            StringComparison.Ordinal);
        Assert.Contains("BehaviorsModule.HostExecutorName", testingAppHost, StringComparison.Ordinal);
        Assert.Contains(".WaitFor(silo)", testingAppHost, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "DigitalBrain.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                $"Could not find DigitalBrain.slnx above {AppContext.BaseDirectory}.");
    }
}
