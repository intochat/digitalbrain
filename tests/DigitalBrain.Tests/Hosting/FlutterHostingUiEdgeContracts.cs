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
        "WithUiEdge projects " + FlutterHostingExtensions.DefaultUiResourceName
        + " as AsClient with named http endpoint, exclusive owner env; omits flutter host")]
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
            .WithHttpEndpoint(name: FlutterHostingExtensions.UiHttpEndpointName)
            .WithReference(brain);

        var ui = Assert.Single(
            builder.Resources.OfType<ProjectResource>(),
            resource => resource.Name == FlutterHostingExtensions.DefaultUiResourceName);

        FlutterHostingProjectionSupport.AssertNoFlutterHost(builder);
        FlutterHostingProjectionSupport.AssertUiHasNamedHttpEndpoint(ui);

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
}
