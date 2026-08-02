using Aspire.Hosting;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Shell;
using DigitalBrain.Shell.Aspire.Hosting;
using Xunit;

namespace DigitalBrain.Tests.Hosting;

public sealed class ShellHostingSelectionContracts
{
    [Fact(DisplayName =
        "omit ShellModule → runtime graph has no digitalbrain-ui / digitalbrain-flutter")]
    public void OmitFlutterModuleProjectsNoOSSurfaceResources()
    {
        var builder = DistributedApplication.CreateBuilder();
        var brain = builder.AddDigitalBrain("brain");

        _ = builder
            .AddContainer("silo", "mcr.microsoft.com/dotnet/runtime")
            .WithHttpEndpoint(name: ShellHostingExtensions.UiEdgeEndpointName)
            .WithReference(brain);

        ShellHostingProjectionSupport.AssertNoOSSurfaceResources(builder);
    }

    [Fact(DisplayName =
        "ShellModule without With* is vocabulary-only: silo lists module, runtime graph has no OS surface")]
    public async Task VocabularyOnlySelectionDoesNotStartOSSurface()
    {
        var builder = DistributedApplication.CreateBuilder();
        var brain = builder.AddDigitalBrain("brain");
        brain.AddModule<ShellModule>();

        var silo = builder
            .AddContainer("silo", "mcr.microsoft.com/dotnet/runtime")
            .WithHttpEndpoint(name: ShellHostingExtensions.UiEdgeEndpointName)
            .WithReference(brain);

        var siloEnvironment = await ShellHostingProjectionSupport
            .EnvironmentOf(silo.Resource)
            .ConfigureAwait(true);
        Assert.Contains(
            siloEnvironment,
            entry => entry.Key.StartsWith("DigitalBrain__Modules__", StringComparison.Ordinal)
                && string.Equals(
                    entry.Value?.ToString(),
                    ShellModule.Id.Value,
                    StringComparison.Ordinal));

        ShellHostingProjectionSupport.AssertNoOSSurfaceResources(builder);
    }
}
