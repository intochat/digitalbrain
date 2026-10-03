using System.Text.Json;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Collection;
using DigitalBrain.Flutter.ImageCanvas;
using DigitalBrain.Flutter.Layout;
using DigitalBrain.Flutter.Surface;

namespace DigitalBrain.Modules.Flutter.Tests.Unit.Composition;

public sealed class PersistedCollectionFacts
{
    [Fact]
    public async Task CollectionAndLayoutPreserveOrderAndExtentsAfterReactivation()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<FlutterModule>().StartAsync(ct);
        var collection = brain.Get<ICollectionView>("collections/items");
        await collection.Set(new([new("b", "Second", "file"), new("a", "First", "file")], Cursor: "next"), 0);
        await brain.DeactivateAsync(collection, ct);
        var restored = (await collection.Read()).Definition;
        Assert.Equal(["b", "a"], restored.Items.Select(item => item.Id));
        Assert.Equal("next", restored.Cursor);
        var layout = brain.Get<ILayout>("collections/layout");
        await layout.Set(new("column", [new("collection", "collections/items"), new("text", "collections/status")], Extents: [0, 48]), 0);
        await brain.DeactivateAsync(layout, ct);
        var extents = (await layout.Read()).Definition.Extents;
        Assert.NotNull(extents);
        Assert.Equal([0d, 48d], extents);
        await layout.Set(new("row", []), 1);
        await brain.DeactivateAsync(layout, ct);
        Assert.Null((await layout.Read()).Definition.Extents);
    }

    [Fact]
    public void ArrayFieldsRetainTheCamelCaseJsonShapeAndNullableExtents()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        const string json = """{"mode":"column","children":[{"kind":"text","name":"status"}],"gap":12,"extents":null}""";
        var layout = JsonSerializer.Deserialize<LayoutDefinition>(json, options)!;
        Assert.Equal("status", Assert.Single(layout.Children).Name);
        Assert.Null(layout.Extents);
        using var serialized = JsonDocument.Parse(JsonSerializer.Serialize(layout, options));
        Assert.Equal(JsonValueKind.Array, serialized.RootElement.GetProperty("children").ValueKind);
        Assert.Equal(JsonValueKind.Null, serialized.RootElement.GetProperty("extents").ValueKind);
        const string recipeJson = """{"crop":null,"strokes":[{"colorArgb":4294901760,"width":3,"points":[{"x":20,"y":30}]}]}""";
        var recipe = JsonSerializer.Deserialize<ImageRecipe>(recipeJson, options)!;
        Assert.Equal(new ImagePoint(20, 30), Assert.Single(Assert.Single(recipe.Strokes).Points));
        Assert.Equal(recipeJson, JsonSerializer.Serialize(recipe, options));
    }
}
