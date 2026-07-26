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
            .WithHttpEndpoint(name: FlutterHostingExtensions.UiHttpEndpointName)
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
            .WithHttpEndpoint(name: FlutterHostingExtensions.UiHttpEndpointName)
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
}
