using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Aspire.Hosting;
using Xunit;

namespace DigitalBrain.Tests.Hosting;

public sealed class FlutterHostingUIEdgeContracts
{
    [Fact(DisplayName =
        "WithUIEdge projects " + FlutterHostingExtensions.DefaultUIResourceName
        + " as AsClient with named http endpoint, exclusive owner env; omits flutter host")]
    public async Task WithUIEdgeProjectsNamedHttpEndpointAsClientOnly()
    {
        var builder = DistributedApplication.CreateBuilder();
        var brain = builder.AddDigitalBrain("brain");

        brain.AddModule<FlutterModule>(flutter => flutter.WithUIEdge(options =>
        {
            options.ProjectPath = FlutterHostingProjectionSupport.UIProjectPath;
            options.Owner = "edge-owner";
        }));

        var silo = builder
            .AddContainer("silo", "mcr.microsoft.com/dotnet/runtime")
            .WithHttpEndpoint(name: FlutterHostingExtensions.UIHttpEndpointName)
            .WithReference(brain);

        var ui = Assert.Single(
            builder.Resources.OfType<ProjectResource>(),
            resource => resource.Name == FlutterHostingExtensions.DefaultUIResourceName);

        FlutterHostingProjectionSupport.AssertNoFlutterHost(builder);
        FlutterHostingProjectionSupport.AssertUIHasNamedHttpEndpoint(ui);

        var environment = await FlutterHostingProjectionSupport.EnvironmentOf(ui).ConfigureAwait(true);
        FlutterHostingProjectionSupport.AssertExclusiveUIProductEnvironment(environment);
        Assert.Equal(
            "edge-owner",
            environment[FlutterHostingExtensions.OwnerEnvironmentVariable]?.ToString());

        Assert.Contains(
            ui.Annotations.OfType<WaitAnnotation>(),
            wait => wait.WaitType == WaitType.WaitUntilHealthy
                && ReferenceEquals(wait.Resource, silo.Resource));
    }
}
