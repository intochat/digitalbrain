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
        "WithFlutterHost headless projects dart host with exclusive DIGITALBRAIN_UI_BASE + DIGITALBRAIN_SHELL")]
    public async Task WithFlutterHostHeadlessProjectsEdgeUrlOnly()
    {
        var builder = DistributedApplication.CreateBuilder();
        var brain = builder.AddDigitalBrain("brain");

        brain.AddModule<FlutterModule>(flutter => flutter
            .WithUiEdge(options => options.ProjectPath = FlutterHostingProjectionSupport.UiProjectPath)
            .WithFlutterHost(options =>
            {
                options.WorkingDirectory = FlutterHostingProjectionSupport.FlutterClientDirectory;
                options.Mode = FlutterHostMode.Headless;
                options.RequireHost = true;
                options.ShellName = "desk";
            }));

        _ = builder
            .AddContainer("silo", "mcr.microsoft.com/dotnet/runtime")
            .WithHttpEndpoint(name: "http")
            .WithReference(brain);

        var host = Assert.Single(
            builder.Resources.OfType<ExecutableResource>(),
            resource => resource.Name == FlutterHostingExtensions.DefaultFlutterResourceName);

        Assert.Equal("dart", host.Command, StringComparer.OrdinalIgnoreCase);
        var args = await FlutterHostingProjectionSupport.ResolvedArgsOf(host).ConfigureAwait(true);
        Assert.Contains(
            args,
            arg => string.Equals(
                arg,
                FlutterHostingExtensions.HeadlessHostEntry,
                StringComparison.Ordinal));

        var environment = await FlutterHostingProjectionSupport.EnvironmentKeysOf(host).ConfigureAwait(true);
        FlutterHostingProjectionSupport.AssertExclusiveFlutterHostEnvironment(environment);

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
        "WithFlutterHost Auto without Flutter project markers projects headless dart host")]
    public async Task WithFlutterHostAutoWithoutProjectMarkersProjectsHeadless()
    {
        var builder = DistributedApplication.CreateBuilder();
        var brain = builder.AddDigitalBrain("brain");
        var headlessPackage = Directory.CreateTempSubdirectory("db-flutter-headless-");
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(headlessPackage.FullName, "pubspec.yaml"),
                "name: digitalbrain_flutter_headless_fixture\n",
                TestContext.Current.CancellationToken)
                .ConfigureAwait(true);
            Directory.CreateDirectory(Path.Combine(headlessPackage.FullName, "bin"));
            await File.WriteAllTextAsync(
                Path.Combine(headlessPackage.FullName, "bin", "digitalbrain_host.dart"),
                "void main() {}\n",
                TestContext.Current.CancellationToken)
                .ConfigureAwait(true);

            Assert.False(
                File.Exists(Path.Combine(headlessPackage.FullName, "lib", "main.dart")));
            Assert.False(
                Directory.Exists(Path.Combine(headlessPackage.FullName, "windows")));

            brain.AddModule<FlutterModule>(flutter => flutter
                .WithUiEdge(options => options.ProjectPath = FlutterHostingProjectionSupport.UiProjectPath)
                .WithFlutterHost(options =>
                {
                    options.WorkingDirectory = headlessPackage.FullName;
                    options.Mode = FlutterHostMode.Auto;
                    options.FlutterCommand = "dotnet";
                    options.RequireHost = true;
                    options.ShellName = "desk";
                }));

            _ = builder
                .AddContainer("silo", "mcr.microsoft.com/dotnet/runtime")
                .WithHttpEndpoint(name: "http")
                .WithReference(brain);

            var host = Assert.Single(
                builder.Resources.OfType<ExecutableResource>(),
                resource => resource.Name == FlutterHostingExtensions.DefaultFlutterResourceName);

            Assert.Equal("dart", host.Command, StringComparer.OrdinalIgnoreCase);
            var args = await FlutterHostingProjectionSupport.ResolvedArgsOf(host).ConfigureAwait(true);
            Assert.Contains(
                args,
                arg => string.Equals(
                    arg,
                    FlutterHostingExtensions.HeadlessHostEntry,
                    StringComparison.Ordinal));
            Assert.DoesNotContain(args, arg => arg is "-d" or "windows");

            var environment = await FlutterHostingProjectionSupport.EnvironmentKeysOf(host).ConfigureAwait(true);
            FlutterHostingProjectionSupport.AssertExclusiveFlutterHostEnvironment(environment);
        }
        finally
        {
            headlessPackage.Delete(recursive: true);
        }
    }

    [Fact(DisplayName =
        "WithFlutterHost Auto on pure-Dart root discovers shell/ markers + CLI → flutter run under shell WorkingDirectory")]
    public async Task WithFlutterHostAutoWithMarkersAndCliProjectsDesktop()
    {
        var builder = DistributedApplication.CreateBuilder();
        var brain = builder.AddDigitalBrain("brain");
        var clientDir = FlutterHostingProjectionSupport.FlutterClientDirectory;
        var shellDir = FlutterHostingProjectionSupport.FlutterShellDirectory;
        await FlutterHostingProjectionSupport.AssertShellDesktopLayoutAsync(
            shellDir,
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        brain.AddModule<FlutterModule>(flutter => flutter
            .WithUiEdge(options => options.ProjectPath = FlutterHostingProjectionSupport.UiProjectPath)
            .WithFlutterHost(options =>
            {
                options.WorkingDirectory = clientDir;
                options.Mode = FlutterHostMode.Auto;
                options.FlutterCommand = "dotnet";
                options.DeviceTarget = "windows";
                options.RequireHost = true;
                options.ShellName = "desk";
            }));

        _ = builder
            .AddContainer("silo", "mcr.microsoft.com/dotnet/runtime")
            .WithHttpEndpoint(name: "http")
            .WithReference(brain);

        var host = Assert.Single(
            builder.Resources.OfType<ExecutableResource>(),
            resource => resource.Name == FlutterHostingExtensions.DefaultFlutterResourceName);

        Assert.Equal("dotnet", host.Command, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(
            Path.GetFullPath(shellDir),
            Path.GetFullPath(host.WorkingDirectory),
            StringComparer.OrdinalIgnoreCase);
        var args = await FlutterHostingProjectionSupport.ResolvedArgsOf(host).ConfigureAwait(true);
        Assert.Contains(args, arg => arg == "run");
        Assert.Contains(args, arg => arg == "-d");
        Assert.Contains(args, arg => arg == "windows");
        Assert.DoesNotContain(
            args,
            arg => string.Equals(
                arg,
                FlutterHostingExtensions.HeadlessHostEntry,
                StringComparison.Ordinal));

        var environment = await FlutterHostingProjectionSupport.EnvironmentKeysOf(host).ConfigureAwait(true);
        FlutterHostingProjectionSupport.AssertExclusiveFlutterHostEnvironment(environment);
    }

    [Fact(DisplayName =
        "WithFlutterHost FlutterDesktop on pure-Dart root runs under discovered shell WorkingDirectory")]
    public async Task WithFlutterHostDesktopProjectsFlutterRun()
    {
        var builder = DistributedApplication.CreateBuilder();
        var brain = builder.AddDigitalBrain("brain");
        var clientDir = FlutterHostingProjectionSupport.FlutterClientDirectory;
        var shellDir = FlutterHostingProjectionSupport.FlutterShellDirectory;
        await FlutterHostingProjectionSupport.AssertShellDesktopLayoutAsync(
            shellDir,
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        brain.AddModule<FlutterModule>(flutter => flutter
            .WithUiEdge(options => options.ProjectPath = FlutterHostingProjectionSupport.UiProjectPath)
            .WithFlutterHost(options =>
            {
                options.WorkingDirectory = clientDir;
                options.Mode = FlutterHostMode.FlutterDesktop;
                options.FlutterCommand = "flutter";
                options.DeviceTarget = "windows";
                options.RequireHost = true;
            }));

        _ = builder
            .AddContainer("silo", "mcr.microsoft.com/dotnet/runtime")
            .WithHttpEndpoint(name: "http")
            .WithReference(brain);

        var host = Assert.Single(
            builder.Resources.OfType<ExecutableResource>(),
            resource => resource.Name == FlutterHostingExtensions.DefaultFlutterResourceName);

        Assert.Equal("flutter", host.Command, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(
            Path.GetFullPath(shellDir),
            Path.GetFullPath(host.WorkingDirectory),
            StringComparer.OrdinalIgnoreCase);
        var args = await FlutterHostingProjectionSupport.ResolvedArgsOf(host).ConfigureAwait(true);
        Assert.Contains(args, arg => arg == "run");
        Assert.Contains(args, arg => arg == "windows");

        var environment = await FlutterHostingProjectionSupport.EnvironmentKeysOf(host).ConfigureAwait(true);
        FlutterHostingProjectionSupport.AssertExclusiveFlutterHostEnvironment(environment);
    }

    [Fact(DisplayName =
        "WithFlutterHost Auto on pure-Dart client + missing Flutter CLI still projects headless dart host")]
    public async Task WithFlutterHostAutoPureDartWithoutCliProjectsHeadless()
    {
        var builder = DistributedApplication.CreateBuilder();
        var brain = builder.AddDigitalBrain("brain");
        var clientDir = FlutterHostingProjectionSupport.FlutterClientDirectory;
        var missingFlutterCli = Path.Combine(
            Path.GetTempPath(),
            "digitalbrain-missing-flutter-" + Guid.NewGuid().ToString("N"),
            "flutter-not-installed.exe");

        Assert.False(File.Exists(missingFlutterCli));
        await FlutterHostingProjectionSupport.AssertPureDartClientLayoutAsync(
            clientDir,
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        brain.AddModule<FlutterModule>(flutter => flutter
            .WithUiEdge(options => options.ProjectPath = FlutterHostingProjectionSupport.UiProjectPath)
            .WithFlutterHost(options =>
            {
                options.WorkingDirectory = clientDir;
                options.Mode = FlutterHostMode.Auto;
                options.FlutterCommand = missingFlutterCli;
                options.RequireHost = true;
                options.ShellName = "desk";
            }));

        _ = builder
            .AddContainer("silo", "mcr.microsoft.com/dotnet/runtime")
            .WithHttpEndpoint(name: "http")
            .WithReference(brain);

        var host = Assert.Single(
            builder.Resources.OfType<ExecutableResource>(),
            resource => resource.Name == FlutterHostingExtensions.DefaultFlutterResourceName);

        Assert.Equal("dart", host.Command, StringComparer.OrdinalIgnoreCase);
        var args = await FlutterHostingProjectionSupport.ResolvedArgsOf(host).ConfigureAwait(true);
        Assert.Contains(
            args,
            arg => string.Equals(
                arg,
                FlutterHostingExtensions.HeadlessHostEntry,
                StringComparison.Ordinal));
        Assert.DoesNotContain(args, arg => arg is "-d" or "windows");
    }

    [Fact(DisplayName =
        "WithFlutterHost Auto + shell desktop package + missing Flutter CLI + RequireHost false omits host honestly")]
    public async Task WithFlutterHostAutoShellWithoutCliOmitsHostWhenNotRequired()
    {
        var builder = DistributedApplication.CreateBuilder();
        var brain = builder.AddDigitalBrain("brain");
        var shellDir = FlutterHostingProjectionSupport.FlutterShellDirectory;
        var missingFlutterCli = Path.Combine(
            Path.GetTempPath(),
            "digitalbrain-missing-flutter-" + Guid.NewGuid().ToString("N"),
            "flutter-not-installed.exe");

        Assert.False(File.Exists(missingFlutterCli));
        await FlutterHostingProjectionSupport.AssertShellDesktopLayoutAsync(
            shellDir,
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        brain.AddModule<FlutterModule>(flutter => flutter
            .WithUiEdge(options => options.ProjectPath = FlutterHostingProjectionSupport.UiProjectPath)
            .WithFlutterHost(options =>
            {
                options.WorkingDirectory = shellDir;
                options.Mode = FlutterHostMode.Auto;
                options.FlutterCommand = missingFlutterCli;
                options.RequireHost = false;
            }));

        _ = builder
            .AddContainer("silo", "mcr.microsoft.com/dotnet/runtime")
            .WithHttpEndpoint(name: "http")
            .WithReference(brain);

        var ui = Assert.Single(
            builder.Resources.OfType<ProjectResource>(),
            resource => resource.Name == FlutterHostingExtensions.DefaultUiResourceName);
        FlutterHostingProjectionSupport.AssertUiHasNamedHttpEndpoint(ui);
        FlutterHostingProjectionSupport.AssertNoFlutterHost(builder);
    }

    [Fact(DisplayName =
        "WithFlutterHost Auto + shell desktop package + missing Flutter CLI + RequireHost true throws")]
    public async Task WithFlutterHostAutoShellWithoutCliThrowsWhenHostRequired()
    {
        var builder = DistributedApplication.CreateBuilder();
        var brain = builder.AddDigitalBrain("brain");
        var shellDir = FlutterHostingProjectionSupport.FlutterShellDirectory;
        var missingFlutterCli = Path.Combine(
            Path.GetTempPath(),
            "digitalbrain-missing-flutter-" + Guid.NewGuid().ToString("N"),
            "flutter-not-installed.exe");

        Assert.False(File.Exists(missingFlutterCli));
        await FlutterHostingProjectionSupport.AssertShellDesktopLayoutAsync(
            shellDir,
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            brain.AddModule<FlutterModule>(flutter => flutter
                .WithUiEdge(options => options.ProjectPath = FlutterHostingProjectionSupport.UiProjectPath)
                .WithFlutterHost(options =>
                {
                    options.WorkingDirectory = shellDir;
                    options.Mode = FlutterHostMode.Auto;
                    options.FlutterCommand = missingFlutterCli;
                    options.RequireHost = true;
                })));

        Assert.Contains("no runnable Auto path", exception.Message, StringComparison.Ordinal);
        FlutterHostingProjectionSupport.AssertNoFlutterHost(builder);
    }

    [Fact(DisplayName =
        "WithFlutterHost missing package + RequireHost false omits host and still projects Ui edge")]
    public void WithFlutterHostMissingPackageOmitsHostWhenNotRequired()
    {
        var builder = DistributedApplication.CreateBuilder();
        var brain = builder.AddDigitalBrain("brain");
        var missingPackage = Path.Combine(
            Path.GetTempPath(),
            "digitalbrain-missing-flutter-pkg-" + Guid.NewGuid().ToString("N"));

        Assert.False(Directory.Exists(missingPackage));

        brain.AddModule<FlutterModule>(flutter => flutter
            .WithUiEdge(options => options.ProjectPath = FlutterHostingProjectionSupport.UiProjectPath)
            .WithFlutterHost(options =>
            {
                options.WorkingDirectory = missingPackage;
                options.Mode = FlutterHostMode.Auto;
                options.RequireHost = false;
            }));

        _ = builder
            .AddContainer("silo", "mcr.microsoft.com/dotnet/runtime")
            .WithHttpEndpoint(name: "http")
            .WithReference(brain);

        var ui = Assert.Single(
            builder.Resources.OfType<ProjectResource>(),
            resource => resource.Name == FlutterHostingExtensions.DefaultUiResourceName);
        FlutterHostingProjectionSupport.AssertUiHasNamedHttpEndpoint(ui);
        FlutterHostingProjectionSupport.AssertNoFlutterHost(builder);
    }
}
