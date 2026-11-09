using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using DigitalBrain.Invocation;
using DigitalBrain.UI;

namespace DigitalBrain.Tests.Unit.UI;

public sealed class ViewModelJsonFacts
{
    private static readonly string[] ActionMembers = ["Emit", "Intent", "Resolve", "OnSubmit"];

    private static readonly Type[] ActionCarriers = [typeof(Button), typeof(TabItem), typeof(Switch), typeof(SelectOption), typeof(Field), typeof(ApprovalCardNode), typeof(NavItemNode)];

    private static bool CarriesAction(Type type) => type.GetProperties().Any(p => ActionMembers.Contains(p.Name)
        || p.PropertyType == typeof(Emit) || Nullable.GetUnderlyingType(p.PropertyType) == typeof(Emit)
        || p.PropertyType == typeof(Intent) || p.PropertyType == typeof(Resolve));

    [Fact]
    public void EveryInteractiveNodeHasAKey()
    {
        foreach (var type in ActionCarriers)
        {
            Assert.True(CarriesAction(type), $"{type.Name} carries no action.");
            Assert.True(type.GetProperty("Key")?.PropertyType == typeof(string), $"{type.Name} has no string Key.");
        }
    }

    [Fact]
    public void OnlyInteractiveTypesDeclareAnAction()
    {
        var model = typeof(Node).Assembly.GetTypes().Where(t => t.Namespace == typeof(Node).Namespace && t.IsPublic && t != typeof(Emit) && t != typeof(Resolve) && t != typeof(Intent));

        Assert.Equal(ActionCarriers.Select(t => t.Name).Order(StringComparer.Ordinal), model.Where(CarriesAction).Select(t => t.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void NoEmitTargetsTheConfirmationsResolve()
    {
        string? root = AppContext.BaseDirectory;
        while (root is not null && !Directory.Exists(Path.Combine(root, "src", "DigitalBrain.UI"))) { root = Path.GetDirectoryName(root); }
        Assert.NotNull(root);
        var needle = "new Emit(\"" + "confirmations\"";
        var sep = Path.DirectorySeparatorChar;
        var offenders = new[] { "src" + sep + "DigitalBrain.UI", "src" + sep + "DigitalBrain.Testing", "tests" }
            .SelectMany(dir => Directory.EnumerateFiles(Path.Combine(root!, dir), "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains(sep + "obj" + sep) && !f.Contains(sep + "bin" + sep))
            .Where(f => File.ReadAllText(f).Contains(needle, StringComparison.Ordinal))
            .ToList();
        Assert.Empty(offenders);
    }

    [Fact]
    public void ButtonRoundTripsWithEmit()
    {
        var view = new View("v", new Column([new Button("go", "Go", new Emit("app.files", "files.open", JsonSerializer.SerializeToElement(new { id = 1 })))]));
        var options = RuntimeProtocol.OptionsFor(typeof(View));

        var element = JsonSerializer.SerializeToElement(view, options);
        var button = element.GetProperty("root").GetProperty("children")[0];

        Assert.Equal("button", button.GetProperty("kind").GetString());
        Assert.Equal("go", button.GetProperty("key").GetString());
        var emit = button.GetProperty("emit");
        Assert.Equal(("app.files", "files.open", 1), (emit.GetProperty("owner").GetString(), emit.GetProperty("signal").GetString(), emit.GetProperty("args").GetProperty("id").GetInt32()));
        var found = ViewNodes.Find(element, "go");
        Assert.NotNull(found);
        var (owner, signal, args) = ViewNodes.EmitOf(found.Value)!.Value;
        Assert.Equal(("app.files", "files.open", 1), (owner, signal, args!.Value.GetProperty("id").GetInt32()));
        var again = Assert.IsType<Button>(Assert.IsType<Column>(JsonSerializer.Deserialize<View>(element.GetRawText(), options)!.Root).Children[0]);
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
