using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.AI;
using DigitalBrain.AI.Aspire.Hosting;
using DigitalBrain.AI.Ollama;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Shell;
using DigitalBrain.Shell.Aspire.Hosting;
using Xunit;

namespace DigitalBrain.Tests.Hosting;

public sealed class FlutterHostingUiHttpContracts
{
    [Fact(DisplayName =
        "WithUiEdge projects " + ShellHostingExtensions.DefaultUIResourceName
        + " as a client with late-bound module topology and no silo secrets")]
    public async Task WithUiEdgeProjectsLateBoundModuleTopologyWithoutSiloSecrets()
    {
        var builder = DistributedApplication.CreateBuilder();
        var brain = builder.AddDigitalBrain("brain");

        brain.AddModule<ShellModule>(flutter => flutter.WithUiEdge(options =>
        {
            options.ProjectPath = FlutterHostingProjectionSupport.UIProjectPath;
            options.Owner = "ui-owner";
        }));
        brain.AddModule<AIModule>(ai => ai.WithLlm<Gemma4>());

        var silo = builder
            .AddContainer("silo", "mcr.microsoft.com/dotnet/runtime")
            .WithHttpEndpoint(name: ShellHostingExtensions.UiEdgeEndpointName)
            .WithReference(brain);

        var ui = Assert.Single(
            builder.Resources.OfType<ProjectResource>(),
            resource => resource.Name == ShellHostingExtensions.DefaultUIResourceName);

        FlutterHostingProjectionSupport.AssertNoFlutterHost(builder);
        FlutterHostingProjectionSupport.AssertUIHasNamedHttpEndpoint(ui);

        var environment = await FlutterHostingProjectionSupport.EnvironmentOf(ui).ConfigureAwait(true);
        FlutterHostingProjectionSupport.AssertClientSafeUIProductEnvironment(
            environment,
            [ShellModule.Id.Value, AIModule.Id.Value]);
        Assert.Equal(
            "ui-owner",
            environment[ShellHostingExtensions.OwnerEnvironmentVariable]?.ToString());
        Assert.DoesNotContain(
            FlutterHostingProjectionSupport.JournalConnectionEnvironmentKey,
            environment.Keys);

        Assert.Contains(
            ui.Annotations.OfType<WaitAnnotation>(),
            wait => wait.WaitType == WaitType.WaitUntilHealthy
                && ReferenceEquals(wait.Resource, silo.Resource));
    }
}
