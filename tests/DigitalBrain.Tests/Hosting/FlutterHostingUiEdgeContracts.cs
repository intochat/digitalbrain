using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Aspire.Hosting;
using Xunit;

namespace DigitalBrain.Tests.Hosting;

public sealed class FlutterHostingUiEdgeContracts
{
    [Fact(DisplayName =
        "WithUiEdge projects digitalbrain-ui as AsClient with named http endpoint, exclusive owner env; omits flutter host")]
    public async Task WithUiEdgeProjectsNamedHttpEndpointAsClientOnly()
    {
        var builder = DistributedApplication.CreateBuilder();
        var brain = builder.AddDigitalBrain("brain");

        brain.AddModule<FlutterModule>(flutter => flutter.WithUiEdge(options =>
        {
            options.ProjectPath = FlutterHostingProjectionSupport.UiProjectPath;
            options.Owner = "edge-owner";
        }));

        var silo = builder
            .AddContainer("silo", "mcr.microsoft.com/dotnet/runtime")
            .WithHttpEndpoint(name: "http")
            .WithReference(brain);

        var ui = Assert.Single(
            builder.Resources.OfType<ProjectResource>(),
            resource => resource.Name == FlutterHostingExtensions.DefaultUiResourceName);

        FlutterHostingProjectionSupport.AssertNoFlutterHost(builder);
        FlutterHostingProjectionSupport.AssertUiHasNamedHttpEndpoint(ui);
        Assert.Equal(
            FlutterHostingExtensions.UiHttpEndpointName,
            "http",
            StringComparer.Ordinal);

        var environment = await FlutterHostingProjectionSupport.EnvironmentOf(ui).ConfigureAwait(true);
        FlutterHostingProjectionSupport.AssertExclusiveUiProductEnvironment(environment);
        Assert.Equal(
            "edge-owner",
            environment[FlutterHostingExtensions.OwnerEnvironmentVariable]?.ToString());

        Assert.Contains(
            ui.Annotations.OfType<WaitAnnotation>(),
            wait => wait.WaitType == WaitType.WaitUntilHealthy
                && ReferenceEquals(wait.Resource, silo.Resource));
    }

    [Fact(DisplayName =
        "OS surface source: Ui is named http + AsClient + owner only; Flutter host is UI_BASE + SHELL only")]
    public void OsSurfaceSourcePinsExclusiveEnvAndNamedHttpEndpoint()
    {
        var hostingDir = Path.Combine(
            FlutterHostingProjectionSupport.RepositoryRoot,
            "modules",
            "DigitalBrain.Modules.Flutter.Aspire.Hosting");
        var source = File.ReadAllText(Path.Combine(hostingDir, "FlutterHostingExtensions.cs"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        var launchSource = File.ReadAllText(Path.Combine(hostingDir, "FlutterHostLaunch.cs"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        var uiEdge = FlutterHostingProjectionSupport.MethodBody(source, "internal void EnsureUiEdge");
        Assert.Contains("brain.AsClient()", uiEdge, StringComparison.Ordinal);
        Assert.Contains("WithHttpEndpoint(name: UiHttpEndpointName)", uiEdge, StringComparison.Ordinal);
        Assert.Contains("WithHttpHealthCheck(UiHealthPath)", uiEdge, StringComparison.Ordinal);
        Assert.Contains("OwnerEnvironmentVariable", uiEdge, StringComparison.Ordinal);
        Assert.Equal(1, FlutterHostingProjectionSupport.CountOccurrences(uiEdge, "WithEnvironment"));
        Assert.DoesNotContain("UiBaseEnvironmentVariable", uiEdge, StringComparison.Ordinal);
        Assert.DoesNotContain("ShellEnvironmentVariable", uiEdge, StringComparison.Ordinal);
        Assert.DoesNotContain("Journal", uiEdge, StringComparison.Ordinal);
        Assert.DoesNotContain("StateProtectionKey", uiEdge, StringComparison.Ordinal);
        Assert.DoesNotContain("Modules", uiEdge, StringComparison.Ordinal);
        Assert.DoesNotContain(
            ".WithReference(brain)",
            uiEdge.Replace(".WithReference(brain.AsClient())", string.Empty, StringComparison.Ordinal),
            StringComparison.Ordinal);

        var flutterHost = FlutterHostingProjectionSupport.MethodBody(source, "internal void EnsureFlutterHost");
        Assert.Contains("ui.GetEndpoint(UiHttpEndpointName)", flutterHost, StringComparison.Ordinal);
        Assert.Contains("FlutterHostLaunch.Resolve", flutterHost, StringComparison.Ordinal);
        Assert.Contains("launch.WorkingDirectory", flutterHost, StringComparison.Ordinal);
        Assert.Equal(2, FlutterHostingProjectionSupport.CountOccurrences(flutterHost, "WithEnvironment"));
        Assert.Contains("UiBaseEnvironmentVariable", flutterHost, StringComparison.Ordinal);
        Assert.Contains("ShellEnvironmentVariable", flutterHost, StringComparison.Ordinal);
        Assert.DoesNotContain("OwnerEnvironmentVariable", flutterHost, StringComparison.Ordinal);
        Assert.DoesNotContain("AsClient", flutterHost, StringComparison.Ordinal);
        Assert.DoesNotContain("WithReference", flutterHost, StringComparison.Ordinal);
        Assert.DoesNotContain("Journal", flutterHost, StringComparison.Ordinal);
        Assert.DoesNotContain("StateProtectionKey", flutterHost, StringComparison.Ordinal);

        Assert.Contains("ShellPackageDirectoryName = \"shell\"", launchSource, StringComparison.Ordinal);
        Assert.Contains("ResolveDesktopPackageDirectory", launchSource, StringComparison.Ordinal);

        Assert.Contains(
            "public const string UiHttpEndpointName = \"http\";",
            source,
            StringComparison.Ordinal);
    }
}
