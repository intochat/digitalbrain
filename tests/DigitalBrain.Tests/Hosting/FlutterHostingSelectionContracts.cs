using Aspire.Hosting;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Aspire.Hosting;
using Xunit;

namespace DigitalBrain.Tests.Hosting;

public sealed class FlutterHostingSelectionContracts
{
    [Fact(DisplayName =
        "omit FlutterModule → runtime graph has no digitalbrain-ui / digitalbrain-flutter")]
    public void OmitFlutterModuleProjectsNoOsSurfaceResources()
    {
        var builder = DistributedApplication.CreateBuilder();
        var brain = builder.AddDigitalBrain("brain");

        _ = builder
            .AddContainer("silo", "mcr.microsoft.com/dotnet/runtime")
            .WithHttpEndpoint(name: "http")
            .WithReference(brain);

        FlutterHostingProjectionSupport.AssertNoOsSurfaceResources(builder);
    }

    [Fact(DisplayName =
        "FlutterModule without With* is vocabulary-only: silo lists module, runtime graph has no OS surface")]
    public async Task VocabularyOnlySelectionDoesNotStartOsSurface()
    {
        var builder = DistributedApplication.CreateBuilder();
        var brain = builder.AddDigitalBrain("brain");
        brain.AddModule<FlutterModule>();

        var silo = builder
            .AddContainer("silo", "mcr.microsoft.com/dotnet/runtime")
            .WithHttpEndpoint(name: "http")
            .WithReference(brain);

        var siloEnvironment = await FlutterHostingProjectionSupport
            .EnvironmentOf(silo.Resource)
            .ConfigureAwait(true);
        Assert.Contains(
            siloEnvironment,
            entry => entry.Key.StartsWith("DigitalBrain__Modules__", StringComparison.Ordinal)
                && string.Equals(
                    entry.Value?.ToString(),
                    FlutterModule.Id.Value,
                    StringComparison.Ordinal));

        FlutterHostingProjectionSupport.AssertNoOsSurfaceResources(builder);
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
            FlutterHostingProjectionSupport.RepositoryRoot,
            "hosts",
            "DigitalBrain.AppHost",
            "AppHost.cs"));

        Assert.Contains("AddModule<FlutterModule>", appHost, StringComparison.Ordinal);
        Assert.Contains("WithUiEdge", appHost, StringComparison.Ordinal);
        Assert.Contains("WithFlutterHost", appHost, StringComparison.Ordinal);
        FlutterHostingProjectionSupport.AssertNoOsSurfaceHandWire(appHost);
    }

    [Fact(DisplayName =
        "Quickstart and Testing AppHosts omit OS surface selection and never hand-wire Ui/Flutter")]
    public void CompanionAppHostsOmitOsSurfaceAndHandWire()
    {
        var root = FlutterHostingProjectionSupport.RepositoryRoot;
        var quickstart = File.ReadAllText(Path.Combine(
            root,
            "hosts",
            "DigitalBrain.Quickstart.AppHost",
            "AppHost.cs"));
        var testing = File.ReadAllText(Path.Combine(
            root,
            "hosts",
            "DigitalBrain.TestingAppHost",
            "AppHost.cs"));
        var quickstartProject = File.ReadAllText(Path.Combine(
            root,
            "hosts",
            "DigitalBrain.Quickstart.AppHost",
            "DigitalBrain.Quickstart.AppHost.csproj"));
        var testingProject = File.ReadAllText(Path.Combine(
            root,
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
            FlutterHostingProjectionSupport.AssertNoOsSurfaceHandWire(appHost);
        }

        foreach (var project in new[] { quickstartProject, testingProject })
        {
            Assert.DoesNotContain("DigitalBrain.Modules.Flutter", project, StringComparison.Ordinal);
            Assert.DoesNotContain("DigitalBrain.Ui", project, StringComparison.Ordinal);
            Assert.DoesNotContain("Flutter.Aspire.Hosting", project, StringComparison.Ordinal);
        }
    }
}
