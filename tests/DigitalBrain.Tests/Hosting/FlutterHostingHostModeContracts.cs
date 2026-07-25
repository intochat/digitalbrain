using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Aspire.Hosting;
using Xunit;

namespace DigitalBrain.Tests.Hosting;

public sealed class FlutterHostingHostModeContracts
{
    [Fact(DisplayName =
        "WithFlutterHost defaults to Desktop — flutter run under shell/ with exclusive edge env")]
    public async Task WithFlutterHostDefaultsToDesktopShell()
    {
        var builder = DistributedApplication.CreateBuilder();
        var brain = builder.AddDigitalBrain("brain");
        await FlutterHostingProjectionSupport.AssertShellDesktopLayoutAsync(
            FlutterHostingProjectionSupport.FlutterShellDirectory,
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        const string pinnedFlutterCommand = "flutter";
        brain.AddModule<FlutterModule>(flutter => flutter
            .WithUiEdge(options => options.ProjectPath = FlutterHostingProjectionSupport.UiProjectPath)
            .WithFlutterHost(options =>
            {
                options.WorkingDirectory = FlutterHostingProjectionSupport.FlutterClientDirectory;
                options.FlutterCommand = pinnedFlutterCommand;
            }));

        _ = builder
            .AddContainer("silo", "mcr.microsoft.com/dotnet/runtime")
            .WithHttpEndpoint(name: FlutterHostingExtensions.UiHttpEndpointName)
            .WithReference(brain);

        var host = Assert.Single(
            builder.Resources.OfType<ExecutableResource>(),
            resource => resource.Name == FlutterHostingExtensions.DefaultFlutterResourceName);

        Assert.Equal(pinnedFlutterCommand, host.Command, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(
            Path.GetFullPath(FlutterHostingProjectionSupport.FlutterShellDirectory),
            Path.GetFullPath(host.WorkingDirectory),
            StringComparer.OrdinalIgnoreCase);
        var args = await FlutterHostingProjectionSupport.ResolvedArgsOf(host).ConfigureAwait(true);
        Assert.Equal(["run", "-d", FlutterHostingExtensions.DefaultDeviceTarget], args);

        var environment = await FlutterHostingProjectionSupport.EnvironmentOf(host).ConfigureAwait(true);
        FlutterHostingProjectionSupport.AssertExclusiveFlutterHostEnvironment(
            environment.Keys.ToHashSet(StringComparer.Ordinal));
        Assert.Equal(
            FlutterHostingExtensions.DefaultShellName,
            environment[FlutterHostingExtensions.ShellEnvironmentVariable]?.ToString());

        var ui = Assert.Single(
            builder.Resources,
            resource => resource.Name == FlutterHostingExtensions.DefaultUiResourceName);
        FlutterHostingProjectionSupport.AssertUiHasNamedHttpEndpoint(ui);
        Assert.Contains(
            host.Annotations.OfType<WaitAnnotation>(),
            wait => wait.WaitType == WaitType.WaitUntilHealthy
                && ReferenceEquals(wait.Resource, ui));
    }

    [Fact(DisplayName =
        "WithFlutterHost<DesktopHost> is the same product sentence as WithFlutterHost()")]
    public async Task WithFlutterHostDesktopHostMatchesDefault()
    {
        var builder = DistributedApplication.CreateBuilder();
        var brain = builder.AddDigitalBrain("brain");

        const string pinnedFlutterCommand = "dotnet";
        brain.AddModule<FlutterModule>(flutter => flutter
            .WithUiEdge(options => options.ProjectPath = FlutterHostingProjectionSupport.UiProjectPath)
            .WithFlutterHost<DesktopHost>(options =>
            {
                options.WorkingDirectory = FlutterHostingProjectionSupport.FlutterClientDirectory;
                options.FlutterCommand = pinnedFlutterCommand;
            }));

        _ = builder
            .AddContainer("silo", "mcr.microsoft.com/dotnet/runtime")
            .WithHttpEndpoint(name: FlutterHostingExtensions.UiHttpEndpointName)
            .WithReference(brain);

        var host = Assert.Single(
            builder.Resources.OfType<ExecutableResource>(),
            resource => resource.Name == FlutterHostingExtensions.DefaultFlutterResourceName);
        Assert.Equal(pinnedFlutterCommand, host.Command, StringComparer.Ordinal);
        Assert.Equal(
            Path.GetFullPath(FlutterHostingProjectionSupport.FlutterShellDirectory),
            Path.GetFullPath(host.WorkingDirectory),
            StringComparer.OrdinalIgnoreCase);
        var args = await FlutterHostingProjectionSupport.ResolvedArgsOf(host).ConfigureAwait(true);
        Assert.Equal(["run", "-d", FlutterHostingExtensions.DefaultDeviceTarget], args);
    }

    [Fact(DisplayName =
        "WithFlutterHost<HeadlessHost> projects dart run bin/digitalbrain_host.dart with exclusive edge env")]
    public async Task WithFlutterHostHeadlessHostProjectsDartRun()
    {
        var builder = DistributedApplication.CreateBuilder();
        var brain = builder.AddDigitalBrain("brain");
        await FlutterHostingProjectionSupport.AssertPureDartClientLayoutAsync(
            FlutterHostingProjectionSupport.FlutterClientDirectory,
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        const string pinnedDartCommand = "dart";
        brain.AddModule<FlutterModule>(flutter => flutter
            .WithUiEdge(options => options.ProjectPath = FlutterHostingProjectionSupport.UiProjectPath)
            .WithFlutterHost<HeadlessHost>(options =>
            {
                options.WorkingDirectory = FlutterHostingProjectionSupport.FlutterClientDirectory;
                options.DartCommand = pinnedDartCommand;
            }));

        _ = builder
            .AddContainer("silo", "mcr.microsoft.com/dotnet/runtime")
            .WithHttpEndpoint(name: FlutterHostingExtensions.UiHttpEndpointName)
            .WithReference(brain);

        var host = Assert.Single(
            builder.Resources.OfType<ExecutableResource>(),
            resource => resource.Name == FlutterHostingExtensions.DefaultFlutterResourceName);

        Assert.Equal(pinnedDartCommand, host.Command, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(
            Path.GetFullPath(FlutterHostingProjectionSupport.FlutterClientDirectory),
            Path.GetFullPath(host.WorkingDirectory),
            StringComparer.OrdinalIgnoreCase);
        var args = await FlutterHostingProjectionSupport.ResolvedArgsOf(host).ConfigureAwait(true);
        Assert.Equal(["run", FlutterHostingExtensions.HeadlessHostEntry], args);
        Assert.DoesNotContain(
            args,
            arg => arg is "-d" || arg == FlutterHostingExtensions.DefaultDeviceTarget);

        var environment = await FlutterHostingProjectionSupport.EnvironmentOf(host).ConfigureAwait(true);
        FlutterHostingProjectionSupport.AssertExclusiveFlutterHostEnvironment(
            environment.Keys.ToHashSet(StringComparer.Ordinal));
        Assert.Equal(
            FlutterHostingExtensions.DefaultShellName,
            environment[FlutterHostingExtensions.ShellEnvironmentVariable]?.ToString());
    }

    [Fact(DisplayName =
        "WithFlutterHost Desktop without desktop markers throws — no silent headless fallback")]
    public void WithFlutterHostDesktopWithoutMarkersThrows()
    {
        var builder = DistributedApplication.CreateBuilder();
        var brain = builder.AddDigitalBrain("brain");
        var headlessOnly = Directory.CreateTempSubdirectory("db-flutter-headless-only-");
        try
        {
            File.WriteAllText(
                Path.Combine(headlessOnly.FullName, "pubspec.yaml"),
                "name: headless_only\n");
            var headlessEntry = Path.Combine(
                headlessOnly.FullName,
                FlutterHostingExtensions.HeadlessHostEntry.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(headlessEntry)!);
            File.WriteAllText(headlessEntry, "void main() {}\n");

            var exception = Assert.Throws<InvalidOperationException>(() =>
                brain.AddModule<FlutterModule>(flutter => flutter
                    .WithUiEdge(options => options.ProjectPath = FlutterHostingProjectionSupport.UiProjectPath)
                    .WithFlutterHost(options => options.WorkingDirectory = headlessOnly.FullName)));

            Assert.Contains(nameof(HeadlessHost), exception.Message, StringComparison.Ordinal);
            FlutterHostingProjectionSupport.AssertNoFlutterHost(builder);
        }
        finally
        {
            headlessOnly.Delete(recursive: true);
        }
    }

    [Fact(DisplayName =
        "WithFlutterHost Headless without bin host entry throws — no silent omit")]
    public void WithFlutterHostHeadlessWithoutEntryThrows()
    {
        var builder = DistributedApplication.CreateBuilder();
        var brain = builder.AddDigitalBrain("brain");
        var emptyPackage = Directory.CreateTempSubdirectory("db-flutter-empty-");
        try
        {
            File.WriteAllText(
                Path.Combine(emptyPackage.FullName, "pubspec.yaml"),
                "name: empty\n");

            var exception = Assert.Throws<InvalidOperationException>(() =>
                brain.AddModule<FlutterModule>(flutter => flutter
                    .WithUiEdge(options => options.ProjectPath = FlutterHostingProjectionSupport.UiProjectPath)
                    .WithFlutterHost<HeadlessHost>(options =>
                        options.WorkingDirectory = emptyPackage.FullName)));

            Assert.Contains(
                FlutterHostingExtensions.HeadlessHostEntry,
                exception.Message,
                StringComparison.Ordinal);
            FlutterHostingProjectionSupport.AssertNoFlutterHost(builder);
        }
        finally
        {
            emptyPackage.Delete(recursive: true);
        }
    }

    [Fact(DisplayName =
        "WithFlutterHost missing package throws — no silent omit")]
    public void WithFlutterHostMissingPackageThrows()
    {
        var builder = DistributedApplication.CreateBuilder();
        var brain = builder.AddDigitalBrain("brain");
        var missing = Path.Combine(
            Path.GetTempPath(),
            "digitalbrain-missing-flutter-" + Guid.NewGuid().ToString("N"));

        _ = Assert.Throws<InvalidOperationException>(() =>
            brain.AddModule<FlutterModule>(flutter => flutter
                .WithUiEdge(options => options.ProjectPath = FlutterHostingProjectionSupport.UiProjectPath)
                .WithFlutterHost(options => options.WorkingDirectory = missing)));

        FlutterHostingProjectionSupport.AssertNoFlutterHost(builder);
    }
}
