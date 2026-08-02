using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Shell;
using DigitalBrain.Shell.Aspire.Hosting;
using Xunit;

namespace DigitalBrain.Tests.Hosting;

public sealed class FlutterHostingHostModeContracts
{
    [Fact(DisplayName =
        "WithWindowHost projects flutter run under shell/ with exclusive edge env")]
    public async Task WithWindowHostProjectsFlutterRunUnderShell()
    {
        var builder = DistributedApplication.CreateBuilder();
        var brain = builder.AddDigitalBrain("brain");
        await FlutterHostingProjectionSupport.AssertShellDesktopLayoutAsync(
            FlutterHostingProjectionSupport.FlutterShellDirectory,
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        const string pinnedFlutterCommand = "flutter";
        brain.AddModule<ShellModule>(flutter => flutter
            .WithUiEdge(options => options.ProjectPath = FlutterHostingProjectionSupport.UIProjectPath)
            .WithWindowHost(options =>
            {
                options.WorkingDirectory = FlutterHostingProjectionSupport.FlutterClientDirectory;
                options.FlutterCommand = pinnedFlutterCommand;
            }));

        _ = builder
            .AddContainer("silo", "mcr.microsoft.com/dotnet/runtime")
            .WithHttpEndpoint(name: ShellHostingExtensions.UiEdgeEndpointName)
            .WithReference(brain);

        var host = Assert.Single(
            builder.Resources.OfType<ExecutableResource>(),
            resource => resource.Name == ShellHostingExtensions.DefaultFlutterResourceName);

        Assert.Equal(pinnedFlutterCommand, host.Command, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(
            Path.GetFullPath(FlutterHostingProjectionSupport.FlutterShellDirectory),
            Path.GetFullPath(host.WorkingDirectory),
            StringComparer.OrdinalIgnoreCase);
        var args = await FlutterHostingProjectionSupport.ResolvedArgsOf(host).ConfigureAwait(true);
        Assert.Equal(["run", "-d", ShellHostingExtensions.DefaultDeviceTarget], args);

        var environment = await FlutterHostingProjectionSupport.EnvironmentOf(host).ConfigureAwait(true);
        FlutterHostingProjectionSupport.AssertExclusiveFlutterHostEnvironment(
            environment.Keys.ToHashSet(StringComparer.Ordinal));
        Assert.Equal(
            ShellHostingExtensions.DefaultShellName,
            environment[ShellHostingExtensions.ShellEnvironmentVariable]?.ToString());

        var ui = Assert.Single(
            builder.Resources,
            resource => resource.Name == ShellHostingExtensions.DefaultUIResourceName);
        FlutterHostingProjectionSupport.AssertUIHasNamedHttpEndpoint(ui);
        Assert.Contains(
            host.Annotations.OfType<WaitAnnotation>(),
            wait => wait.WaitType == WaitType.WaitUntilHealthy
                && ReferenceEquals(wait.Resource, ui));
    }

    [Fact(DisplayName =
        "WithHeadlessHost projects dart run bin/digitalbrain_host.dart with exclusive edge env")]
    public async Task WithHeadlessHostProjectsDartRun()
    {
        var builder = DistributedApplication.CreateBuilder();
        var brain = builder.AddDigitalBrain("brain");
        await FlutterHostingProjectionSupport.AssertPureDartClientLayoutAsync(
            FlutterHostingProjectionSupport.FlutterClientDirectory,
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        const string pinnedDartCommand = "dart";
        brain.AddModule<ShellModule>(flutter => flutter
            .WithUiEdge(options => options.ProjectPath = FlutterHostingProjectionSupport.UIProjectPath)
            .WithHeadlessHost(options =>
            {
                options.WorkingDirectory = FlutterHostingProjectionSupport.FlutterClientDirectory;
                options.DartCommand = pinnedDartCommand;
            }));

        _ = builder
            .AddContainer("silo", "mcr.microsoft.com/dotnet/runtime")
            .WithHttpEndpoint(name: ShellHostingExtensions.UiEdgeEndpointName)
            .WithReference(brain);

        var host = Assert.Single(
            builder.Resources.OfType<ExecutableResource>(),
            resource => resource.Name == ShellHostingExtensions.DefaultFlutterResourceName);

        Assert.Equal(pinnedDartCommand, host.Command, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(
            Path.GetFullPath(FlutterHostingProjectionSupport.FlutterClientDirectory),
            Path.GetFullPath(host.WorkingDirectory),
            StringComparer.OrdinalIgnoreCase);
        var args = await FlutterHostingProjectionSupport.ResolvedArgsOf(host).ConfigureAwait(true);
        Assert.Equal(["run", ShellHostingExtensions.HeadlessHostEntry], args);
        Assert.DoesNotContain(
            args,
            arg => arg is "-d" || arg == ShellHostingExtensions.DefaultDeviceTarget);

        var environment = await FlutterHostingProjectionSupport.EnvironmentOf(host).ConfigureAwait(true);
        FlutterHostingProjectionSupport.AssertExclusiveFlutterHostEnvironment(
            environment.Keys.ToHashSet(StringComparer.Ordinal));
        Assert.Equal(
            ShellHostingExtensions.DefaultShellName,
            environment[ShellHostingExtensions.ShellEnvironmentVariable]?.ToString());
    }

    [Fact(DisplayName =
        "WithWindowHost without window markers throws — no silent headless fallback")]
    public void WithWindowHostWithoutMarkersThrows()
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
                ShellHostingExtensions.HeadlessHostEntry.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(headlessEntry)!);
            File.WriteAllText(headlessEntry, "void main() {}\n");

            var exception = Assert.Throws<InvalidOperationException>(() =>
                brain.AddModule<ShellModule>(flutter => flutter
                    .WithUiEdge(options => options.ProjectPath = FlutterHostingProjectionSupport.UIProjectPath)
                    .WithWindowHost(options => options.WorkingDirectory = headlessOnly.FullName)));

            Assert.Contains(nameof(ShellHostingExtensions.WithHeadlessHost), exception.Message, StringComparison.Ordinal);
            FlutterHostingProjectionSupport.AssertNoFlutterHost(builder);
        }
        finally
        {
            headlessOnly.Delete(recursive: true);
        }
    }

    [Fact(DisplayName =
        "WithHeadlessHost without bin host entry throws — no silent omit")]
    public void WithHeadlessHostWithoutEntryThrows()
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
                brain.AddModule<ShellModule>(flutter => flutter
                    .WithUiEdge(options => options.ProjectPath = FlutterHostingProjectionSupport.UIProjectPath)
                    .WithHeadlessHost(options =>
                        options.WorkingDirectory = emptyPackage.FullName)));

            Assert.Contains(
                ShellHostingExtensions.HeadlessHostEntry,
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
        "WithWindowHost missing package throws — no silent omit")]
    public void WithWindowHostMissingPackageThrows()
    {
        var builder = DistributedApplication.CreateBuilder();
        var brain = builder.AddDigitalBrain("brain");
        var missing = Path.Combine(
            Path.GetTempPath(),
            "digitalbrain-missing-flutter-" + Guid.NewGuid().ToString("N"));

        _ = Assert.Throws<InvalidOperationException>(() =>
            brain.AddModule<ShellModule>(flutter => flutter
                .WithUiEdge(options => options.ProjectPath = FlutterHostingProjectionSupport.UIProjectPath)
                .WithWindowHost(options => options.WorkingDirectory = missing)));

        FlutterHostingProjectionSupport.AssertNoFlutterHost(builder);
    }

    [Fact(DisplayName =
        "WithWebHost projects flutter run -d chrome under shell/ with dart-defines and exclusive edge env")]
    public async Task WithWebHostProjectsFlutterRunUnderShell()
    {
        var builder = DistributedApplication.CreateBuilder();
        var brain = builder.AddDigitalBrain("brain");
        await FlutterHostingProjectionSupport.AssertShellWebLayoutAsync(
            FlutterHostingProjectionSupport.FlutterShellDirectory,
            TestContext.Current.CancellationToken).ConfigureAwait(true);

        const string pinnedFlutterCommand = "flutter";
        brain.AddModule<ShellModule>(flutter => flutter
            .WithUiEdge(options => options.ProjectPath = FlutterHostingProjectionSupport.UIProjectPath)
            .WithWebHost(options =>
            {
                options.WorkingDirectory = FlutterHostingProjectionSupport.FlutterClientDirectory;
                options.FlutterCommand = pinnedFlutterCommand;
            }));

        _ = builder
            .AddContainer("silo", "mcr.microsoft.com/dotnet/runtime")
            .WithHttpEndpoint(name: ShellHostingExtensions.UiEdgeEndpointName)
            .WithReference(brain);

        var host = Assert.Single(
            builder.Resources.OfType<ExecutableResource>(),
            resource => resource.Name == ShellHostingExtensions.DefaultFlutterResourceName);

        Assert.Equal(pinnedFlutterCommand, host.Command, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(
            Path.GetFullPath(FlutterHostingProjectionSupport.FlutterShellDirectory),
            Path.GetFullPath(host.WorkingDirectory),
            StringComparer.OrdinalIgnoreCase);
        var args = await FlutterHostingProjectionSupport.ResolvedArgsOf(host).ConfigureAwait(true);
        Assert.Equal(
            ["run", "-d", ShellHostingExtensions.DefaultWebDeviceTarget],
            args.Take(3).ToArray());
        Assert.Contains(
            $"--dart-define={ShellHostingExtensions.ShellEnvironmentVariable}={ShellHostingExtensions.DefaultShellName}",
            args,
            StringComparer.Ordinal);
        Assert.Contains(
            $"--dart-define={ShellHostingExtensions.ChatEnvironmentVariable}={ShellHostingExtensions.DefaultChatName}",
            args,
            StringComparer.Ordinal);

        // UI base is an endpoint ReferenceExpression (resolved by DCP at run); still a dart-define carrier.
        var rawArgs = await FlutterHostingProjectionSupport.RawArgsOf(host).ConfigureAwait(true);
        Assert.Contains(
            rawArgs,
            arg => arg is ReferenceExpression expression
                && expression.ValueExpression.Contains(
                    ShellHostingExtensions.UIBaseEnvironmentVariable,
                    StringComparison.Ordinal));

        var environment = await FlutterHostingProjectionSupport.EnvironmentOf(host).ConfigureAwait(true);
        FlutterHostingProjectionSupport.AssertExclusiveFlutterHostEnvironment(
            environment.Keys.ToHashSet(StringComparer.Ordinal));
    }

    [Fact(DisplayName =
        "WithWebHost without web markers throws — no silent window fallback")]
    public void WithWebHostWithoutMarkersThrows()
    {
        var builder = DistributedApplication.CreateBuilder();
        var brain = builder.AddDigitalBrain("brain");
        var windowOnly = Directory.CreateTempSubdirectory("db-flutter-window-only-");
        try
        {
            File.WriteAllText(
                Path.Combine(windowOnly.FullName, "pubspec.yaml"),
                "name: window_only\n");
            Directory.CreateDirectory(Path.Combine(windowOnly.FullName, "lib"));
            File.WriteAllText(Path.Combine(windowOnly.FullName, "lib", "main.dart"), "void main() {}\n");
            Directory.CreateDirectory(
                Path.Combine(windowOnly.FullName, ShellHostingExtensions.DefaultDeviceTarget));

            var exception = Assert.Throws<InvalidOperationException>(() =>
                brain.AddModule<ShellModule>(flutter => flutter
                    .WithUiEdge(options => options.ProjectPath = FlutterHostingProjectionSupport.UIProjectPath)
                    .WithWebHost(options => options.WorkingDirectory = windowOnly.FullName)));

            Assert.Contains(nameof(ShellHostingExtensions.WithWindowHost), exception.Message, StringComparison.Ordinal);
            FlutterHostingProjectionSupport.AssertNoFlutterHost(builder);
        }
        finally
        {
            windowOnly.Delete(recursive: true);
        }
    }
}
