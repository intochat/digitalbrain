using System.Text.Json;
using DigitalBrain.UI;

namespace DigitalBrain.Tests.Unit;

public sealed class ViewModelFacts
{
    [Fact]
    public void View_round_trips_through_json()
    {
        var view = new View("Files", new Column([
            new Text("Recent"),
            new Collection([new Entry("a", "Report", "today"), new Entry("b", "Notes", null)]),
            new TextField("search", null, "Search"),
            new Toast("Saved"),
        ]));

        var json = JsonSerializer.Serialize(view);
        var back = JsonSerializer.Deserialize<View>(json);

        Assert.NotNull(back);
        Assert.Equal("Files", back.Title);
        var column = Assert.IsType<Column>(back.Root);
        Assert.Equal(4, column.Children.Count);
        var collection = Assert.IsType<Collection>(column.Children[1]);
        Assert.Equal(new Entry("a", "Report", "today"), collection.Entries[0]);
        Assert.Equal(new TextField("search", null, "Search"), column.Children[2]);
        Assert.Contains("\"kind\":\"column\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Records_compare_by_value()
    {
        Assert.Equal(new Entry("a", "Report", null), new Entry("a", "Report", null));
        Assert.Equal(new Toast("x"), new Toast("x"));
    }
}
