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
    public void ProductSurfaceResourceExists(string resourceName)
    {
        var resource = fixture.Model.Resource(resourceName);

        Assert.Equal(resourceName, resource.Name);
    }

    [Fact]
    public void McpWaitsForKernel()
    {
        var mcp = fixture.Model.Resource(ProductSurfaceResourceNames.Mcp);
        var kernel = fixture.Model.Resource(ProductSurfaceResourceNames.Kernel);

        Assert.Contains(kernel, mcp.Annotations.OfType<WaitAnnotation>().Select(static wait => wait.Resource));
    }
}
