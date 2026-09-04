using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Abstractions;
using DigitalBrain.Aspire.Hosting;
using Xunit;

namespace DigitalBrain.Aspire.Tests;

// Anti-rot conformance (spec section 6.1): the AppHost model must keep declaring the resources
// and dependency edges the product actually relies on. Model-only, no containers, milliseconds.
[Collection(ModelCollection.Name)]
public sealed class TopologyConformanceTests(ModelFixture fixture)
{
    [Theory]
    [InlineData(DigitalBrainNames.Clustering)]
    [InlineData(DigitalBrainNames.Reminders)]
    [InlineData(DigitalBrainNames.Journal)]
    [InlineData(DigitalBrainNames.GrainState)]
    public void FabricResourceExists(string resourceName)
    {
        // Model.Resource throws with the available names when the resource is missing — that
        // throw IS the existence assertion; the connection-string check adds the shape the
        // fabric contract relies on (kernel consumes each fabric resource as a connection string).
        var resource = fixture.Model.Resource(resourceName);

        Assert.IsAssignableFrom<IResourceWithConnectionString>(resource);
    }

    [Fact]
    public void StorageResourceExists()
    {
        // The emulator parent exposes no connection string itself (its blob/table children do,
        // covered above) — existence is the assertion, via Resource's throwing lookup.
        Assert.NotNull(fixture.Model.Resource(DigitalBrainNames.Storage));
    }

    [Theory]
    [InlineData(ProductSurfaceResourceNames.Kernel)]
    [InlineData(ProductSurfaceResourceNames.Mcp)]
    [InlineData(ProductSurfaceResourceNames.Scripting)]
    public void ProductSurfaceResourceExists(string resourceName)
    {
        var resource = fixture.Model.Resource(resourceName);

        Assert.IsAssignableFrom<ProjectResource>(resource);
    }

    [Fact]
    public void McpWaitsForKernel()
    {
        var mcp = fixture.Model.Resource(ProductSurfaceResourceNames.Mcp);
        var kernel = fixture.Model.Resource(ProductSurfaceResourceNames.Kernel);

        Assert.Contains(kernel, mcp.Annotations.OfType<WaitAnnotation>().Select(static wait => wait.Resource));
    }

    [Fact]
    public void ScriptingWaitsForKernel()
    {
        var scripting = fixture.Model.Resource(ProductSurfaceResourceNames.Scripting);
        var kernel = fixture.Model.Resource(ProductSurfaceResourceNames.Kernel);

        Assert.Contains(kernel, scripting.Annotations.OfType<WaitAnnotation>().Select(static wait => wait.Resource));
    }

    [Fact]
    public void BrainResourceExistsAndParentsTheFabric()
    {
        var brain = fixture.Model.Resource(ProductSurfaceResourceNames.Brain);
        var storage = fixture.Model.Resource(DigitalBrainNames.Storage);

        Assert.IsType<DigitalBrainResource>(brain);
        Assert.Contains(
            storage.Annotations.OfType<ResourceRelationshipAnnotation>(),
            relationship => relationship.Resource == brain && relationship.Type == "Parent");
    }
}
