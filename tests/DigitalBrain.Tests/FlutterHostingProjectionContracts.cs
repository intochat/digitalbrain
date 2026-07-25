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
        "WithUiEdge projects digitalbrain-ui as AsClient with exclusive owner product env")]
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

        var environment = await EnvironmentOf(ui).ConfigureAwait(true);
        AssertExclusiveUiProductEnvironment(environment);
        Assert.Equal(
            "edge-owner",
            environment[FlutterHostingExtensions.OwnerEnvironmentVariable]?.ToString());

        Assert.Contains(
            ui.Annotations.OfType<WaitAnnotation>(),
            wait => wait.WaitType == WaitType.WaitUntilHealthy
                && ReferenceEquals(wait.Resource, silo.Resource));
    }

    [Fact(DisplayName =
        "OS surface source: Ui is AsClient + owner only; Flutter host is UI_BASE + SHELL only")]
    public void OsSurfaceSourcePinsExclusiveEnvComposition()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "modules",
            "DigitalBrain.Modules.Flutter.Aspire.Hosting",
            "FlutterHostingExtensions.cs"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        var uiEdge = MethodBody(source, "internal void EnsureUiEdge");
        Assert.Contains("brain.AsClient()", uiEdge, StringComparison.Ordinal);
        Assert.Contains("OwnerEnvironmentVariable", uiEdge, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(uiEdge, "WithEnvironment"));
        Assert.DoesNotContain("UiBaseEnvironmentVariable", uiEdge, StringComparison.Ordinal);
        Assert.DoesNotContain("ShellEnvironmentVariable", uiEdge, StringComparison.Ordinal);
        Assert.DoesNotContain("Journal", uiEdge, StringComparison.Ordinal);
        Assert.DoesNotContain("StateProtectionKey", uiEdge, StringComparison.Ordinal);
        Assert.DoesNotContain("Modules", uiEdge, StringComparison.Ordinal);
        Assert.DoesNotContain(
            ".WithReference(brain)",
            uiEdge.Replace(".WithReference(brain.AsClient())", string.Empty, StringComparison.Ordinal),
            StringComparison.Ordinal);

        var flutterHost = MethodBody(source, "internal void EnsureFlutterHost");
        Assert.Equal(2, CountOccurrences(flutterHost, "WithEnvironment"));
        Assert.Contains("UiBaseEnvironmentVariable", flutterHost, StringComparison.Ordinal);
        Assert.Contains("ShellEnvironmentVariable", flutterHost, StringComparison.Ordinal);
        Assert.Contains(".WaitFor(ui)", flutterHost, StringComparison.Ordinal);
        Assert.DoesNotContain("OwnerEnvironmentVariable", flutterHost, StringComparison.Ordinal);
        Assert.DoesNotContain("AsClient", flutterHost, StringComparison.Ordinal);
        Assert.DoesNotContain("WithReference", flutterHost, StringComparison.Ordinal);
        Assert.DoesNotContain("Journal", flutterHost, StringComparison.Ordinal);
        Assert.DoesNotContain("StateProtectionKey", flutterHost, StringComparison.Ordinal);
    }

    [Fact(DisplayName =
        "Auto mode short-circuits on project markers before spawning a Flutter CLI probe")]
    public void AutoModeProbesMarkersBeforeFlutterCli()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "modules",
            "DigitalBrain.Modules.Flutter.Aspire.Hosting",
            "FlutterHostingExtensions.cs"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        var resolve = MethodBody(source, "private static HostLaunch? ResolveHostLaunch");
        Assert.Contains(
            "HasFlutterDesktopProjectMarker(workingDirectory) && FlutterCliAvailable(options)",
            resolve,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "FlutterCliAvailable(options) && HasFlutterDesktopProjectMarker(workingDirectory)",
            resolve,
            StringComparison.Ordinal);
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

        var silo = builder
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
        Assert.DoesNotContain(
            host.Annotations.OfType<WaitAnnotation>(),
            wait => ReferenceEquals(wait.Resource, silo.Resource));
    }

    [Fact(DisplayName =
        "WithFlutterHost RequireHost true throws when package directory is missing")]
    public void WithFlutterHostRequireHostThrowsWhenPackageMissing()
    {
        var builder = DistributedApplication.CreateBuilder();
        var brain = builder.AddDigitalBrain("brain");
        var uiProject = Path.Combine(
            RepositoryRoot,
            "hosts",
            "DigitalBrain.Ui",
            "DigitalBrain.Ui.csproj");
        var missingPackage = Path.Combine(
            Path.GetTempPath(),
            "digitalbrain-flutter-missing-" + Guid.NewGuid().ToString("N"));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            brain.AddModule<FlutterModule>(flutter => flutter
                .WithUiEdge(options => options.ProjectPath = uiProject)
                .WithFlutterHost(options =>
                {
                    options.WorkingDirectory = missingPackage;
                    options.RequireHost = true;
                })));

        Assert.Contains("was not found", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            builder.Resources,
            resource => resource.Name == FlutterHostingExtensions.DefaultFlutterResourceName);
    }

    [Fact(DisplayName =
        "WithFlutterHost RequireHost false omits host when package directory is missing")]
    public void WithFlutterHostRequireHostFalseOmitsMissingPackage()
    {
        var builder = DistributedApplication.CreateBuilder();
        var brain = builder.AddDigitalBrain("brain");
        var uiProject = Path.Combine(
            RepositoryRoot,
            "hosts",
            "DigitalBrain.Ui",
            "DigitalBrain.Ui.csproj");
        var missingPackage = Path.Combine(
            Path.GetTempPath(),
            "digitalbrain-flutter-missing-" + Guid.NewGuid().ToString("N"));

        brain.AddModule<FlutterModule>(flutter => flutter
            .WithUiEdge(options => options.ProjectPath = uiProject)
            .WithFlutterHost(options =>
            {
                options.WorkingDirectory = missingPackage;
                options.RequireHost = false;
            }));

        _ = builder
            .AddContainer("silo", "mcr.microsoft.com/dotnet/runtime")
            .WithHttpEndpoint(name: "http")
            .WithReference(brain);

        Assert.Contains(
            builder.Resources,
            resource => resource.Name == FlutterHostingExtensions.DefaultUiResourceName);
        Assert.DoesNotContain(
            builder.Resources,
            resource => resource.Name == FlutterHostingExtensions.DefaultFlutterResourceName);
    }

    [Fact(DisplayName =
        "WithFlutterHost Headless RequireHost true throws when headless entry is missing")]
    public void WithFlutterHostHeadlessRequireHostThrowsWhenEntryMissing()
    {
        var packageDir = Path.Combine(
            Path.GetTempPath(),
            "digitalbrain-flutter-no-entry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(packageDir);
        File.WriteAllText(Path.Combine(packageDir, "pubspec.yaml"), "name: probe\n");

        try
        {
            var builder = DistributedApplication.CreateBuilder();
            var brain = builder.AddDigitalBrain("brain");
            var uiProject = Path.Combine(
                RepositoryRoot,
                "hosts",
                "DigitalBrain.Ui",
                "DigitalBrain.Ui.csproj");

            var exception = Assert.Throws<InvalidOperationException>(() =>
                brain.AddModule<FlutterModule>(flutter => flutter
                    .WithUiEdge(options => options.ProjectPath = uiProject)
                    .WithFlutterHost(options =>
                    {
                        options.WorkingDirectory = packageDir;
                        options.Mode = FlutterHostMode.Headless;
                        options.RequireHost = true;
                    })));

            Assert.Contains("could not be launched", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                builder.Resources,
                resource => resource.Name == FlutterHostingExtensions.DefaultFlutterResourceName);
        }
        finally
        {
            Directory.Delete(packageDir, recursive: true);
        }
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
        var project = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "hosts",
            "DigitalBrain.AppHost",
            "DigitalBrain.AppHost.csproj"));

        Assert.Contains("AddModule<FlutterModule>", appHost, StringComparison.Ordinal);
        Assert.Contains("WithUiEdge", appHost, StringComparison.Ordinal);
        Assert.Contains("WithFlutterHost", appHost, StringComparison.Ordinal);
        AssertNoOsSurfaceHandWire(appHost);

        Assert.Contains(
            "DigitalBrain.Modules.Flutter.Aspire.Hosting",
            project,
            StringComparison.Ordinal);
        Assert.DoesNotContain("DigitalBrain.Ui", project, StringComparison.Ordinal);
        Assert.DoesNotContain("ProbeHost", project, StringComparison.Ordinal);
        Assert.DoesNotContain("ProbeHost", appHost, StringComparison.Ordinal);
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
        var quickstartProject = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "hosts",
            "DigitalBrain.Quickstart.AppHost",
            "DigitalBrain.Quickstart.AppHost.csproj"));
        var testingProject = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "hosts",
            "DigitalBrain.TestingAppHost",
            "DigitalBrain.TestingAppHost.csproj"));

        Assert.Contains("AddModule<QuickstartModule>", quickstart, StringComparison.Ordinal);
        Assert.DoesNotContain("AddModule", testing, StringComparison.Ordinal);

        foreach (var appHost in new[] { quickstart, testing })
        {
            Assert.DoesNotContain("FlutterModule", appHost, StringComparison.Ordinal);
            Assert.DoesNotContain("WithUiEdge", appHost, StringComparison.Ordinal);
            Assert.DoesNotContain("WithFlutterHost", appHost, StringComparison.Ordinal);
            AssertNoOsSurfaceHandWire(appHost);
        }

        foreach (var project in new[] { quickstartProject, testingProject })
        {
            Assert.DoesNotContain("DigitalBrain.Modules.Flutter", project, StringComparison.Ordinal);
            Assert.DoesNotContain("DigitalBrain.Ui", project, StringComparison.Ordinal);
            Assert.DoesNotContain("Flutter.Aspire.Hosting", project, StringComparison.Ordinal);
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

    private static void AssertExclusiveUiProductEnvironment(IReadOnlyDictionary<string, object> environment)
    {
        var productKeys = environment.Keys
            .Where(static key =>
                key.StartsWith("DigitalBrain", StringComparison.Ordinal)
                || key.StartsWith("DIGITALBRAIN", StringComparison.Ordinal)
                || string.Equals(key, "ConnectionStrings__journal", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(
            new HashSet<string>(StringComparer.Ordinal)
            {
                FlutterHostingExtensions.OwnerEnvironmentVariable,
            },
            productKeys);
    }

    private static void AssertNoOsSurfaceHandWire(string appHost)
    {
        Assert.DoesNotContain("Projects.DigitalBrain_Ui", appHost, StringComparison.Ordinal);
        Assert.DoesNotContain("DigitalBrain_Ui", appHost, StringComparison.Ordinal);
        Assert.DoesNotContain("digitalbrain-ui", appHost, StringComparison.Ordinal);
        Assert.DoesNotContain("digitalbrain-flutter", appHost, StringComparison.Ordinal);
        Assert.DoesNotContain("DIGITALBRAIN_UI_BASE", appHost, StringComparison.Ordinal);
        Assert.DoesNotContain("DIGITALBRAIN_SHELL", appHost, StringComparison.Ordinal);
        Assert.DoesNotContain("AddExecutable", appHost, StringComparison.Ordinal);
        Assert.DoesNotContain("ProbeHost", appHost, StringComparison.Ordinal);
    }

    private static async Task<HashSet<string>> EnvironmentKeysOf(IResource resource)
    {
        var environment = await EnvironmentOf(resource).ConfigureAwait(true);
        return environment.Keys.ToHashSet(StringComparer.Ordinal);
    }

    private static async Task<Dictionary<string, object>> EnvironmentOf(IResource resource)
    {
        var execution = new DistributedApplicationExecutionContext(DistributedApplicationOperation.Run);
        var context = new EnvironmentCallbackContext(execution, resource);

        foreach (var annotation in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await annotation.Callback(context).ConfigureAwait(true);
        }

        return new Dictionary<string, object>(context.EnvironmentVariables, StringComparer.Ordinal);
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

    private static string MethodBody(string source, string signatureMarker)
    {
        var signatureIndex = source.IndexOf(signatureMarker, StringComparison.Ordinal);
        Assert.True(signatureIndex >= 0, $"Signature marker '{signatureMarker}' was not found.");

        var openBrace = source.IndexOf('{', signatureIndex);
        Assert.True(openBrace >= 0, $"Opening brace after '{signatureMarker}' was not found.");

        var depth = 0;
        for (var index = openBrace; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source[(openBrace + 1)..index];
                }
            }
        }

        throw new InvalidOperationException($"Could not balance braces for '{signatureMarker}'.");
    }

    private static int CountOccurrences(string source, string token)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }

        return count;
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
