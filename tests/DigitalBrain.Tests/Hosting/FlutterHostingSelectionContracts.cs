using Aspire.Hosting;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Aspire.Hosting;
using DigitalBrain.Tests.Boundary;
using DigitalBrain.Tests.Packages;
using Xunit;

namespace DigitalBrain.Tests.Hosting;

public sealed class FlutterHostingSelectionContracts
{
    private const string QuickstartAppHost = "DigitalBrain.Quickstart.AppHost";
    private const string TestingAppHost = "DigitalBrain.TestingAppHost";

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

    [Fact(DisplayName =
        "product AppHost selects Flutter.Aspire.Hosting; companions cannot project or hand-wire OS surface")]
    public void AppHostsSelectFlutterOsSurfaceOnlyOnProduct()
    {
        var product = PackageBoundarySupport
            .DirectCompileProjectReferencesOf(PackageInventory.ProductAppHost)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains(PackageInventory.ModulesFlutterAspireHosting, product);
        Assert.DoesNotContain(PackageInventory.Ui, product);

        foreach (var companion in new[] { QuickstartAppHost, TestingAppHost })
        {
            var direct = PackageBoundarySupport
                .DirectCompileProjectReferencesOf(companion)
                .ToHashSet(StringComparer.Ordinal);
            Assert.DoesNotContain(PackageInventory.ModulesFlutterAspireHosting, direct);
            Assert.DoesNotContain(PackageInventory.ModulesFlutter, direct);
            Assert.DoesNotContain(PackageInventory.Ui, direct);

            var reachable = PackageBoundarySupport.CompileProjectsReachableFrom(companion);
            Assert.DoesNotContain(PackageInventory.ModulesFlutterAspireHosting, reachable);
            Assert.DoesNotContain(PackageInventory.Ui, reachable);
        }
    }
}
