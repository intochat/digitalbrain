using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Abstractions;
using Xunit;

namespace DigitalBrain.Aspire.Tests;

// Anti-rot conformance (spec section 6.1): the AppHost model must keep declaring the resources
// and dependency edges the product actually relies on. Model-only, no containers, milliseconds.
[Collection(ModelCollection.Name)]
public sealed class TopologyConformanceTests(ModelFixture fixture)
{
    [Theory]
    [InlineData(DigitalBrainNames.Storage)]
    [InlineData(DigitalBrainNames.Clustering)]
    [InlineData(DigitalBrainNames.Reminders)]
    [InlineData(DigitalBrainNames.Journal)]
    [InlineData(DigitalBrainNames.Streams)]
    [InlineData(DigitalBrainNames.PubSub)]
    public void FabricResourceExists(string resourceName)
    {
        var resource = fixture.Model.Resource(resourceName);

        Assert.Equal(resourceName, resource.Name);
    }

    [Theory]
    [InlineData(ProductSurfaceResourceNames.Kernel)]
    [InlineData(ProductSurfaceResourceNames.Mcp)]
    [InlineData(ProductSurfaceResourceNames.Scripting)]
    public void ProductSurfaceResourceExists(string resourceName)
    {
        var resource = fixture.Model.Resource(resourceName);

        Assert.Equal(resourceName, resource.Name);
    }

    [Fact]
    public void ScriptingCarriesExplicitStartupAnnotation()
    {
        // Pins the phase-1 fix: scripting must not auto-run alongside kernel/mcp, only from
        // the dashboard. Aspire 13.5 marks that with ExplicitStartupAnnotation (a presence-only
        // marker, found via Aspire.Hosting.dll reflection: Aspire.Hosting.ApplicationModel.ExplicitStartupAnnotation).
        var scripting = fixture.Model.Resource(ProductSurfaceResourceNames.Scripting);

        Assert.NotEmpty(scripting.Annotations.OfType<ExplicitStartupAnnotation>());
    }

    [Fact]
    public void McpWaitsForKernel()
    {
        var mcp = fixture.Model.Resource(ProductSurfaceResourceNames.Mcp);
        var kernel = fixture.Model.Resource(ProductSurfaceResourceNames.Kernel);

        Assert.Contains(kernel, mcp.Annotations.OfType<WaitAnnotation>().Select(static wait => wait.Resource));
    }
}
