using System.Text.Json;
using DigitalBrain.Invocation;
using DigitalBrain.UI;
using DigitalBrain.Units;
using Xunit;

namespace DigitalBrain.Testing.Tests.Unit.Hosting;

// Presentation assertions over the DigitalBrain.UI value tree, without a renderer.
public sealed class PresentationAssertionFacts
{
    [Fact]
    public void Side_entries_and_their_trailing_text_are_readable()
    {
        var sideView = new View("Storages", new Collection([new Entry("c", "c", null) { Trailing = "10 GB of 100 GB" }]));
        var mainView = new View("Main", new Column([new Text("content")]));
        var side = new PresentedView("side", sideView);
        var main = new PresentedView("main", mainView);

        Assert.True(side.IsSet);
        var entry = Assert.Single(side.Entries);
        Assert.Equal("c", entry.Id);
        Assert.Equal("10 GB of 100 GB", entry.Trailing);
        Assert.Equal("Storages", side.Title);
        Assert.Equal(["content"], main.Texts);
    }

    [Fact]
    public void Find_returns_the_node_or_throws_listing_the_ids_there_are()
    {
        var sideView = new View("Storages", new Collection([new Entry("c", "c", null) { Trailing = "quota unknown" }]));
        var mainView = new View("Main", new Column([new Field("filter", "filter", FieldKind.Text, Placeholder: "Filter names")]));
        var side = new PresentedView("side", sideView);
        var main = new PresentedView("main", mainView);

        Assert.Equal("Filter names", Assert.IsType<Field>(main.Find("filter")).Placeholder);
        Assert.Equal("quota unknown", side.Entry("c").Trailing);
        Assert.IsType<Entry>(side.Find("c"));

        var missing = Assert.Throws<PresentationAssertionException>(() => side.Find("drive"));
        Assert.Contains("Ids: c", missing.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PresentedView_From_PublishedView_deserializes_wire_json()
    {
        var view = new View("Results", new Column([new Text("results for 'report'")]));
        var wire = JsonSerializer.SerializeToElement(view, RuntimeProtocol.OptionsFor(typeof(View)));
        var published = new PublishedView("test", 1, wire);

        var presented = PresentedView.From(published);
        Assert.Equal("Results", presented.Title);
        Assert.Equal(["results for 'report'"], presented.Texts);
        Assert.True(presented.IsSet);
    }

    [Fact]
    public void Lists_every_storage_with_quota()
    {
        var sideView = new View("Storages", new Collection([
            new Entry("c", "c", null) { Trailing = "10 GB of 100 GB" },
            new Entry("drive", "drive", null) { Trailing = "quota unknown" }
        ]));
        var side = new PresentedView("side", sideView);

        Assert.Equal(2, side.Entries.Count);
        Assert.Contains(side.Entries, e => e.Id == "c" && e.Trailing == "10 GB of 100 GB");
        Assert.Contains(side.Entries, e => e.Id == "drive" && e.Trailing == "quota unknown");
    }
}
