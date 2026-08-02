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

public sealed class ShellHostingUiHttpContracts
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
            options.ProjectPath = ShellHostingProjectionSupport.UIProjectPath;
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

        ShellHostingProjectionSupport.AssertNoFlutterHost(builder);
        ShellHostingProjectionSupport.AssertUIHasNamedHttpEndpoint(ui);

        var environment = await ShellHostingProjectionSupport.EnvironmentOf(ui).ConfigureAwait(true);
        ShellHostingProjectionSupport.AssertClientSafeUIProductEnvironment(
            environment,
            [ShellModule.Id.Value, AIModule.Id.Value]);
        Assert.Equal(
            "ui-owner",
            environment[ShellHostingExtensions.OwnerEnvironmentVariable]?.ToString());
        Assert.DoesNotContain(
            ShellHostingProjectionSupport.JournalConnectionEnvironmentKey,
            environment.Keys);

        Assert.Contains(
            ui.Annotations.OfType<WaitAnnotation>(),
            wait => wait.WaitType == WaitType.WaitUntilHealthy
                && ReferenceEquals(wait.Resource, silo.Resource));
    }
}
