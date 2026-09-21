using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Surface;
using DigitalBrain.Flutter.Layout;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.Unit.Composition;

public sealed class CompositionFacts
{
    [Fact]
    public async Task SurfacePreservesOrderedChildrenAndRejectsStaleWritesAfterReactivation()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<FlutterModule>().StartAsync(ct);
        var surface = brain.Get<ISurface>("owner/a/apps/files/surface");
        await surface.Set(new("Files", [new("layout", "owner/a/apps/files/root"), new("text", "owner/a/apps/files/status")]), 0);
        await brain.DeactivateAsync(surface, ct);
        var state = await surface.Read();
        Assert.Equal(1, state.Revision);
        Assert.Equal(["layout", "text"], state.Definition.Children.Select(c => c.Kind));
        await Assert.ThrowsAsync<InvalidOperationException>(() => surface.Set(new("Wrong", []), 0));
        Assert.Equal("Files", (await surface.Read()).Definition.Title);
    }

    [Fact]
    public async Task InvalidCompositionDoesNotReplacePersistedLayout()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<FlutterModule>().StartAsync(ct);
        var layout = brain.Get<ILayout>("owner/a/apps/files/root");
        await layout.Set(new("column", [new("collection", "owner/a/apps/files/items")]), 0);
        await Assert.ThrowsAsync<ArgumentException>(() => layout.Set(new("unknown", []), 1));
        await Assert.ThrowsAsync<ArgumentException>(() => layout.Set(new("row", [], double.NaN), 1));
        Assert.Equal(1, (await layout.Read()).Revision);
    }
}
