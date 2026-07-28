using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.AI;
using DigitalBrain.AI.Aspire.Hosting;
using DigitalBrain.AI.Ollama;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Aspire.Hosting;
using Xunit;

namespace DigitalBrain.Tests.Hosting;

public sealed class FlutterHostingUIEdgeContracts
{
    [Fact(DisplayName =
        "WithUIEdge projects " + FlutterHostingExtensions.DefaultUIResourceName
        + " as a client with late-bound module topology and no silo secrets")]
    public async Task WithUIEdgeProjectsLateBoundModuleTopologyWithoutSiloSecrets()
    {
        var builder = DistributedApplication.CreateBuilder();
        var brain = builder.AddDigitalBrain("brain");

        brain.AddModule<FlutterModule>(flutter => flutter.WithUIEdge(options =>
        {
            options.ProjectPath = FlutterHostingProjectionSupport.UIProjectPath;
            options.Owner = "edge-owner";
        }));
        brain.AddModule<AIModule>(ai => ai.WithLlm<Gemma4>());

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
        FlutterHostingProjectionSupport.AssertClientSafeUIProductEnvironment(
            environment,
            [FlutterModule.Id.Value, AIModule.Id.Value],
            [AIHostingExtensions.Gemma4Feature]);
        Assert.Equal(
            AIHostingExtensions.Gemma4Feature,
            environment["DigitalBrain__ConfiguredFeatures__0"]?.ToString());
        Assert.Equal(
            "edge-owner",
            environment[FlutterHostingExtensions.OwnerEnvironmentVariable]?.ToString());
        Assert.DoesNotContain(
            FlutterHostingProjectionSupport.JournalConnectionEnvironmentKey,
            environment.Keys);

        Assert.Contains(
            ui.Annotations.OfType<WaitAnnotation>(),
            wait => wait.WaitType == WaitType.WaitUntilHealthy
                && ReferenceEquals(wait.Resource, silo.Resource));
    }
}
