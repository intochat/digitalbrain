using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using DigitalBrain.UI;

namespace DigitalBrain.Tests.Unit.UI;

public sealed class ViewModelJsonFacts
{
    private static readonly string[] ActionMembers = ["Emit", "Intent", "Resolve", "OnSubmit"];

    [Fact]
    public void EveryInteractiveNodeHasAKey()
    {
        var nodes = typeof(Node).GetCustomAttributes<JsonDerivedTypeAttribute>().Select(a => a.DerivedType).ToList();
        var interactive = nodes.Where(type => type.GetProperties().Any(p => ActionMembers.Contains(p.Name)
            || p.PropertyType == typeof(Emit) || Nullable.GetUnderlyingType(p.PropertyType) == typeof(Emit))).ToList();

        Assert.Contains(typeof(Button), interactive);
        Assert.Contains(typeof(Field), interactive);
        Assert.Contains(typeof(Switch), interactive);
        Assert.Contains(typeof(ApprovalCardNode), interactive);
        foreach (var type in interactive)
        {
            Assert.True(type.GetProperty("Key")?.PropertyType == typeof(string), $"{type.Name} has no string Key.");
        }
    }

    [Fact]
    public void ButtonRoundTripsWithEmit()
    {
        var view = new View("v", new Column([new Button("go", "Go", new Emit("app.files", "files.open", JsonSerializer.SerializeToElement(new { id = 1 })))]));

        var json = JsonSerializer.Serialize(view);
        using var doc = JsonDocument.Parse(json);
        var button = doc.RootElement.GetProperty("Root").GetProperty("Children")[0];

        Assert.Equal("button", button.GetProperty("kind").GetString());
        Assert.Equal("go", button.GetProperty("Key").GetString());
        Assert.Equal("app.files", button.GetProperty("Emit").GetProperty("Owner").GetString());
        var back = Assert.IsType<Column>(JsonSerializer.Deserialize<View>(json)!.Root);
        var again = Assert.IsType<Button>(back.Children[0]);
        Assert.Equal("files.open", again.Emit!.Signal);
        Assert.Null(again.Intent);
    }

    [Fact]
    public void ButtonCarriesExactlyOneOfEmitOrIntent()
    {
        Assert.Throws<ArgumentException>(() => new Button("a", "A"));
        Assert.Throws<ArgumentException>(() => new Button("a", "A", new Emit("o", "s"), new Intent(IntentKind.Copy, "x")));
        var copy = new Button("c", "Copy", Intent: new Intent(IntentKind.Copy, "x"));

        var json = JsonSerializer.Serialize<Node>(copy);

        Assert.Contains("\"intentKind\":\"Copy\"", json, StringComparison.Ordinal);
        Assert.Equal(IntentKind.Copy, Assert.IsType<Button>(JsonSerializer.Deserialize<Node>(json)).Intent!.Kind);
        Assert.Equal("intent", typeof(Node).GetCustomAttributes<JsonDerivedTypeAttribute>().Single(a => a.DerivedType == typeof(Intent)).TypeDiscriminator);
    }

    [Fact]
    public void FieldIsKeyedAndSerialisesItsKindAsAString()
    {
        var field = new Field("q", "search", FieldKind.Search, "Find", new Emit("app", "search"));

        var json = JsonSerializer.Serialize<Node>(field);

        Assert.Contains("\"kind\":\"field\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Search\"", json, StringComparison.Ordinal);
        Assert.Equal(field, JsonSerializer.Deserialize<Node>(json));
    }

    [Fact]
    public void ApprovalCardResolvesByPlanIdNotByEmit()
    {
        var card = new ApprovalCardNode("k", "p1", "t", "s", []) { Resolve = new Resolve("p1") };

        Assert.DoesNotContain(typeof(ApprovalCardNode).GetProperties(), p => p.PropertyType == typeof(Emit) || Nullable.GetUnderlyingType(p.PropertyType) == typeof(Emit));
        Assert.Equal("p1", Assert.IsType<ApprovalCardNode>(JsonSerializer.Deserialize<Node>(JsonSerializer.Serialize<Node>(card))).Resolve!.PlanId);
    }

    [Fact]
    public void NoTypeNamedButtonActionRemains()
    {
        Assert.DoesNotContain(typeof(Node).Assembly.GetTypes(), type => type.Name == "ButtonAction");
        Assert.DoesNotContain(typeof(Node).Assembly.GetTypes(), type => type.Name == "TextField");
    }
}
