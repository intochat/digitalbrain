using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Aspire.Hosting;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class FlutterHostingProjectionContracts
{
    private static readonly string RepositoryRoot = LocateRepositoryRoot();

    [Fact(DisplayName =
        "FlutterModule without host options projects no OS surface resources")]
    public void VocabularyOnlySelectionDoesNotStartOsSurface()
    {
        var builder = DistributedApplication.CreateBuilder();
        var brain = builder.AddDigitalBrain("brain");
        brain.AddModule<FlutterModule>();

        _ = builder
            .AddContainer("silo", "mcr.microsoft.com/dotnet/runtime")
            .WithHttpEndpoint(name: "http")
            .WithReference(brain);

        Assert.DoesNotContain(
            builder.Resources,
            resource => resource.Name is FlutterHostingExtensions.DefaultUiResourceName
                or FlutterHostingExtensions.DefaultFlutterResourceName);
    }

    [Fact(DisplayName =
        "WithUiEdge projects digitalbrain-ui as AsClient with owner env")]
    public async Task WithUiEdgeProjectsUiEdgeAsClientOnly()
    {
        var builder = DistributedApplication.CreateBuilder();
        var brain = builder.AddDigitalBrain("brain");
        var uiProject = Path.Combine(
            RepositoryRoot,
            "hosts",
            "DigitalBrain.Ui",
            "DigitalBrain.Ui.csproj");

        brain.AddModule<FlutterModule>(flutter => flutter.WithUiEdge(options =>
        {
            options.ProjectPath = uiProject;
            options.Owner = "edge-owner";
        }));

        var silo = builder
            .AddContainer("silo", "mcr.microsoft.com/dotnet/runtime")
            .WithHttpEndpoint(name: "http")
            .WithReference(brain);

        var ui = Assert.Single(
            builder.Resources.OfType<ProjectResource>(),
            resource => resource.Name == FlutterHostingExtensions.DefaultUiResourceName);

        var environment = await EnvironmentKeysOf(ui).ConfigureAwait(true);
        Assert.Contains(FlutterHostingExtensions.OwnerEnvironmentVariable, environment);

        Assert.Contains(
            ui.Annotations.OfType<WaitAnnotation>(),
            wait => wait.WaitType == WaitType.WaitUntilHealthy
                && ReferenceEquals(wait.Resource, silo.Resource));
    }

    [Fact(DisplayName =
        "WithFlutterHost headless projects dart host with exclusive DIGITALBRAIN_UI_BASE + DIGITALBRAIN_SHELL")]
    public async Task WithFlutterHostHeadlessProjectsEdgeUrlOnly()
    {
        var builder = DistributedApplication.CreateBuilder();
        var brain = builder.AddDigitalBrain("brain");
        var uiProject = Path.Combine(
            RepositoryRoot,
            "hosts",
            "DigitalBrain.Ui",
            "DigitalBrain.Ui.csproj");
        var flutterDir = Path.Combine(
            RepositoryRoot,
            "clients",
            "digitalbrain_flutter");

        brain.AddModule<FlutterModule>(flutter => flutter
            .WithUiEdge(options => options.ProjectPath = uiProject)
            .WithFlutterHost(options =>
            {
                options.WorkingDirectory = flutterDir;
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
        var args = await ResolvedArgsOf(host).ConfigureAwait(true);
        Assert.Contains(
            args,
            arg => string.Equals(
                arg,
                FlutterHostingExtensions.HeadlessHostEntry,
                StringComparison.Ordinal));

        var environment = await EnvironmentKeysOf(host).ConfigureAwait(true);
        AssertExclusiveFlutterHostEnvironment(environment);

        var ui = Assert.Single(
            builder.Resources,
            resource => resource.Name == FlutterHostingExtensions.DefaultUiResourceName);
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
        var uiProject = Path.Combine(
            RepositoryRoot,
            "hosts",
            "DigitalBrain.Ui",
            "DigitalBrain.Ui.csproj");
        var flutterDir = Path.Combine(
            RepositoryRoot,
            "clients",
            "digitalbrain_flutter");

        Assert.False(
            File.Exists(Path.Combine(flutterDir, "lib", "main.dart")),
            "clients/digitalbrain_flutter must stay a headless package (no lib/main.dart).");
        Assert.False(
            Directory.Exists(Path.Combine(flutterDir, "windows")),
            "clients/digitalbrain_flutter must stay a headless package (no windows/).");

        brain.AddModule<FlutterModule>(flutter => flutter
            .WithUiEdge(options => options.ProjectPath = uiProject)
            .WithFlutterHost(options =>
            {
                options.WorkingDirectory = flutterDir;
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
        var args = await ResolvedArgsOf(host).ConfigureAwait(true);
        Assert.Contains(
            args,
            arg => string.Equals(
                arg,
                FlutterHostingExtensions.HeadlessHostEntry,
                StringComparison.Ordinal));
        Assert.DoesNotContain(args, arg => arg is "-d" or "windows");

        var environment = await EnvironmentKeysOf(host).ConfigureAwait(true);
        AssertExclusiveFlutterHostEnvironment(environment);
    }

    [Fact(DisplayName =
        "WithFlutterHost FlutterDesktop projects flutter run with exclusive edge env")]
    public async Task WithFlutterHostDesktopProjectsFlutterRun()
    {
        var builder = DistributedApplication.CreateBuilder();
        var brain = builder.AddDigitalBrain("brain");
        var uiProject = Path.Combine(
            RepositoryRoot,
            "hosts",
            "DigitalBrain.Ui",
            "DigitalBrain.Ui.csproj");
        var flutterDir = Path.Combine(
            RepositoryRoot,
            "clients",
            "digitalbrain_flutter");

        brain.AddModule<FlutterModule>(flutter => flutter
            .WithUiEdge(options => options.ProjectPath = uiProject)
            .WithFlutterHost(options =>
            {
                options.WorkingDirectory = flutterDir;
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
        var args = await ResolvedArgsOf(host).ConfigureAwait(true);
        Assert.Contains(args, arg => arg == "run");
        Assert.Contains(args, arg => arg == "windows");

        var environment = await EnvironmentKeysOf(host).ConfigureAwait(true);
        AssertExclusiveFlutterHostEnvironment(environment);
    }

    [Fact(DisplayName =
        "Flutter Aspire.Hosting package is Kernel-free and hosts only via Aspire.Hosting")]
    public void FlutterHostingPackageIsKernelFreeAndHostsViaAspireHosting()
    {
        Assert.Equal(
            "DigitalBrain.Flutter.Aspire.Hosting",
            typeof(FlutterHostingExtensions).Namespace);

        var references = typeof(FlutterHostingExtensions).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain("DigitalBrain.Kernel", references);
        Assert.Contains("DigitalBrain.Aspire.Hosting", references);
        Assert.Contains("DigitalBrain.Modules.Flutter", references);
    }

    [Fact(DisplayName =
        "production AppHost composes OS surface via FlutterModule host options, not hand-wire")]
    public void ProductionAppHostComposesUiThroughModuleHosting()
    {
        var appHost = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "hosts",
            "DigitalBrain.AppHost",
            "AppHost.cs"));

        Assert.Contains("AddModule<FlutterModule>", appHost, StringComparison.Ordinal);
        Assert.Contains("WithUiEdge", appHost, StringComparison.Ordinal);
        Assert.Contains("WithFlutterHost", appHost, StringComparison.Ordinal);
        AssertNoOsSurfaceHandWire(appHost);
    }

    [Fact(DisplayName =
        "Quickstart and Testing AppHosts omit OS surface selection and never hand-wire Ui/Flutter")]
    public void CompanionAppHostsOmitOsSurfaceAndHandWire()
    {
        var quickstart = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "hosts",
            "DigitalBrain.Quickstart.AppHost",
            "AppHost.cs"));
        var testing = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "hosts",
            "DigitalBrain.TestingAppHost",
            "AppHost.cs"));

        foreach (var appHost in new[] { quickstart, testing })
        {
            Assert.DoesNotContain("FlutterModule", appHost, StringComparison.Ordinal);
            Assert.DoesNotContain("WithUiEdge", appHost, StringComparison.Ordinal);
            Assert.DoesNotContain("WithFlutterHost", appHost, StringComparison.Ordinal);
            AssertNoOsSurfaceHandWire(appHost);
        }
    }

    private static void AssertExclusiveFlutterHostEnvironment(HashSet<string> environment)
    {
        Assert.Equal(
            new HashSet<string>(StringComparer.Ordinal)
            {
                FlutterHostingExtensions.UiBaseEnvironmentVariable,
                FlutterHostingExtensions.ShellEnvironmentVariable,
            },
            environment);
    }

    private static void AssertNoOsSurfaceHandWire(string appHost)
    {
        Assert.DoesNotContain(
            "builder.AddProject<Projects.DigitalBrain_Ui>",
            appHost,
            StringComparison.Ordinal);
        Assert.DoesNotContain("digitalbrain-ui", appHost, StringComparison.Ordinal);
        Assert.DoesNotContain("digitalbrain-flutter", appHost, StringComparison.Ordinal);
        Assert.DoesNotContain("DIGITALBRAIN_UI_BASE", appHost, StringComparison.Ordinal);
        Assert.DoesNotContain("DIGITALBRAIN_SHELL", appHost, StringComparison.Ordinal);
        Assert.DoesNotContain("AddExecutable", appHost, StringComparison.Ordinal);
    }

    private static async Task<HashSet<string>> EnvironmentKeysOf(IResource resource)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var execution = new DistributedApplicationExecutionContext(DistributedApplicationOperation.Run);
        var context = new EnvironmentCallbackContext(execution, resource);

        foreach (var annotation in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await annotation.Callback(context).ConfigureAwait(true);
        }

        keys.UnionWith(context.EnvironmentVariables.Keys);
        return keys;
    }

    private static async Task<List<string>> ResolvedArgsOf(ExecutableResource resource)
    {
        var args = new List<object>();
        var context = new CommandLineArgsCallbackContext(args, resource, CancellationToken.None);
        foreach (var annotation in resource.Annotations.OfType<CommandLineArgsCallbackAnnotation>())
        {
            await annotation.Callback(context).ConfigureAwait(true);
        }

        return args.Select(static arg => arg?.ToString() ?? string.Empty).ToList();
    }

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
