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

        Assert.True(
            UiHttpEndpoint(ui).IsProxied,
            "Without a product-chosen port the edge stays behind the Aspire proxy.");
    }

    [Fact(DisplayName = "a product-chosen UI port binds that host port directly instead of an Aspire proxy")]
    public void ProductChosenPortBindsDirectly()
    {
        const int ProductPort = 5080;

        var builder = DistributedApplication.CreateBuilder();
        var brain = builder.AddDigitalBrain("brain");

        brain.AddModule<ShellModule>(flutter => flutter.WithUiEdge(options =>
        {
            options.ProjectPath = ShellHostingProjectionSupport.UIProjectPath;
            options.HttpPort = ProductPort;
        }));

        var ui = Assert.Single(
            builder.Resources.OfType<ProjectResource>(),
            resource => resource.Name == ShellHostingExtensions.DefaultUIResourceName);
        var http = UiHttpEndpoint(ui);

        Assert.Equal(ProductPort, http.Port);
        Assert.False(http.IsProxied);
    }

    private static EndpointAnnotation UiHttpEndpoint(ProjectResource ui)
        => Assert.Single(
            ui.Annotations.OfType<EndpointAnnotation>(),
            endpoint => string.Equals(
                endpoint.Name,
                ShellHostingExtensions.UiEdgeEndpointName,
                StringComparison.Ordinal));
}
