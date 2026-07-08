using DigitalBrain.Core;
using DigitalBrain.Pack.Contracts.UiKit;

namespace DigitalBrain.Tests.Domains;

public class KitExperienceTests
{
    [Fact]
    public void ForExperienceHopTree_carries_tree_and_markers()
    {
        var tree = new UiWidgetTree(UiKitVocabulary.Screen, new Dictionary<string, object?>());
        var surface = UiSurface.ForExperienceHopTree("hello-world", "hello-world", "ask", tree, title: "Hello World");

        Assert.Equal(UiSurface.WidgetTreeKind, surface.Kind);
        Assert.Same(tree, surface.Props["tree"]);
        Assert.Equal("hello-world/hello-world", surface.Props["activeExperience"]);
        Assert.Equal("hello-world", surface.Props["experienceId"]);
        Assert.Equal("ask", surface.Props[UiSurfaceKeys.SurfaceId]);
    }

    private sealed class GreetPack : KitExperience
    {
        protected override UiExperience Define() => Experience("hello-world", "Hello World")
            .Hop("ask", s => s
                .Text("What's your name?")
                .TextField("name", "Your name")
                .Button("Greet", "greeting"))
            .Hop("greeting", s => s
                .Panel(p => p.Text(state =>
                    $"Hello {(state.TryGetValue("name", out var n) && n.Length > 0 ? n : "World")}!")));
    }

    private static ExperienceStep Step(string eventName, params (string, string)[] args) =>
        new("hello-world", "hello-world", eventName, args.ToDictionary(a => a.Item1, a => a.Item2));

    [Fact]
    public void Start_emits_ask_hop_with_text_field_and_button()
    {
        var pack = new GreetPack();
        var outputs = pack.Handle(Step("start"));

        var surface = Assert.IsType<UiSurface>(Assert.Single(outputs));
        Assert.Equal("ask", surface.Props[UiSurfaceKeys.SurfaceId]);
        var tree = Assert.IsType<UiWidgetTree>(surface.Props["tree"]);
        Assert.Equal(UiKitVocabulary.Screen, tree.Type);
        Assert.Collection(tree.Children!,
            n => Assert.Equal(UiKitVocabulary.Text, n.Type),
            n => Assert.Equal(UiKitVocabulary.TextField, n.Type),
            n =>
            {
                Assert.Equal(UiKitVocabulary.Button, n.Type);
                Assert.Equal("greeting", n.Props["eventName"]);
                Assert.Equal("hello-world", n.Props["pack"]);          // injected at emit time
                Assert.Equal("hello-world", n.Props["experienceId"]);
            });
    }

    [Fact]
    public void Greeting_hop_bakes_captured_name_into_text()
    {
        var pack = new GreetPack();
        pack.Handle(Step("start"));
        var outputs = pack.Handle(Step("greeting", ("name", "Alice")));

        var surface = Assert.IsType<UiSurface>(Assert.Single(outputs));
        Assert.Equal("greeting", surface.Props[UiSurfaceKeys.SurfaceId]);
        var tree = Assert.IsType<UiWidgetTree>(surface.Props["tree"]);
        var panel = Assert.Single(tree.Children!);
        Assert.Equal(UiKitVocabulary.Panel, panel.Type);
        var text = Assert.Single(panel.Children!);
        Assert.Equal("Hello Alice!", text.Props["text"]);
    }

    [Fact]
    public void Manifest_handles_experience_step_only()
    {
        var pack = new GreetPack();
        Assert.True(pack.CanHandle(Step("start")));
        Assert.Contains(pack.GetManifest().HandledSynapseTypes, t => t.Value == nameof(ExperienceStep));
    }

    // HelloWorld demo pack source and seeds tests removed (bloat deleted).


    [Fact]
    public void Checkbox_switch_textarea_emit_named_input_nodes()
    {
        var hop = new UiHop("h");
        hop.Checkbox("agree", "I agree").Switch("notify", "Notify me").TextArea("bio", "About you");
        var nodes = hop.Factories.Select(f => f(new Dictionary<string, string>())).ToList();

        Assert.Equal(UiKitVocabulary.Checkbox, nodes[0].Type);
        Assert.Equal("agree", nodes[0].Props["name"]);
        Assert.Equal("I agree", nodes[0].Props["label"]);
        Assert.Equal(UiKitVocabulary.Switch, nodes[1].Type);
        Assert.Equal("notify", nodes[1].Props["name"]);
        Assert.Equal(UiKitVocabulary.TextArea, nodes[2].Type);
        Assert.Equal("About you", nodes[2].Props["placeholder"]);
    }

    [Fact]
    public void Select_radio_slider_datefield_emit_input_nodes_with_options()
    {
        var hop = new UiHop("h");
        hop.Select("color", new[] { "Red", "Green" }, "Color")
           .RadioGroup("size", new[] { "S", "M", "L" })
           .Slider("level", 0, 10, "Level")
           .DateField("when", "When");
        var nodes = hop.Factories.Select(f => f(new Dictionary<string, string>())).ToList();

        Assert.Equal(UiKitVocabulary.Select, nodes[0].Type);
        Assert.Equal("color", nodes[0].Props["name"]);
        var options = Assert.IsAssignableFrom<IReadOnlyList<string>>(nodes[0].Props["options"]);
        Assert.Equal(new[] { "Red", "Green" }, options);
        Assert.Equal(UiKitVocabulary.RadioGroup, nodes[1].Type);
        Assert.Equal(UiKitVocabulary.Slider, nodes[2].Type);
        Assert.Equal(10.0, nodes[2].Props["max"]);
        Assert.Equal(UiKitVocabulary.DateField, nodes[3].Type);
    }

    [Fact]
    public void Layout_nodes_emit_containers_and_leaves()
    {
        var hop = new UiHop("h");
        hop.Row(r => r.Text("a").Text("b")).Divider().Header("Section").Gap(8);
        var nodes = hop.Factories.Select(f => f(new Dictionary<string, string>())).ToList();

        Assert.Equal(UiKitVocabulary.Row, nodes[0].Type);
        Assert.Equal(2, nodes[0].Children!.Count);
        Assert.Equal(UiKitVocabulary.Divider, nodes[1].Type);
        Assert.Equal(UiKitVocabulary.Header, nodes[2].Type);
        Assert.Equal("Section", nodes[2].Props["title"]);
        Assert.Equal(UiKitVocabulary.Gap, nodes[3].Type);
        Assert.Equal(8.0, nodes[3].Props["size"]);
    }

    [Fact]
    public void Display_a_nodes_emit_typed_props()
    {
        var hop = new UiHop("h");
        hop.Heading("Title").Icon("star").Avatar(fallback: "AB").Badge("New");
        var nodes = hop.Factories.Select(f => f(new Dictionary<string, string>())).ToList();
        Assert.Equal(UiKitVocabulary.Heading, nodes[0].Type);
        Assert.Equal("Title", nodes[0].Props["text"]);
        Assert.Equal(UiKitVocabulary.Icon, nodes[1].Type);
        Assert.Equal("star", nodes[1].Props["name"]);
        Assert.Equal(UiKitVocabulary.Avatar, nodes[2].Type);
        Assert.Equal("AB", nodes[2].Props["fallback"]);
        Assert.Equal(UiKitVocabulary.Badge, nodes[3].Type);
        Assert.Equal("New", nodes[3].Props["text"]);
    }

    [Fact]
    public void Tile_with_goTo_is_stamped_with_pack_and_experienceId()
    {
        var pack = new GalleryStubPack();
        var outputs = pack.Handle(new ExperienceStep("p", "p", "start", new Dictionary<string, string>()));
        var tree = (UiWidgetTree)((UiSurface)outputs[0]).Props["tree"];
        var tile = FindByType(tree, UiKitVocabulary.Tile);
        Assert.Equal("p", tile.Props["pack"]);
        Assert.Equal("p", tile.Props["experienceId"]);
        Assert.Equal("next", tile.Props["eventName"]);
    }

    private sealed class GalleryStubPack : KitExperience
    {
        protected override UiExperience Define() => Experience("p", "P")
            .Hop("start", s => s.Tile("Go", goTo: "next"))
            .Hop("next", s => s.Text("done"));
    }

    [Fact]
    public void Feedback_nodes_emit_typed_props()
    {
        var hop = new UiHop("h");
        hop.Alert("Heads up", "details").Progress(0.4).Spinner().Tooltip("hint", t => t.Text("hover me"));
        var nodes = hop.Factories.Select(f => f(new Dictionary<string, string>())).ToList();
        Assert.Equal(UiKitVocabulary.Alert, nodes[0].Type);
        Assert.Equal("Heads up", nodes[0].Props["title"]);
        Assert.Equal(UiKitVocabulary.Progress, nodes[1].Type);
        Assert.Equal(0.4, nodes[1].Props["value"]);
        Assert.Equal(UiKitVocabulary.Spinner, nodes[2].Type);
        Assert.Equal(UiKitVocabulary.Tooltip, nodes[3].Type);
        Assert.Equal("hint", nodes[3].Props["tip"]);
        Assert.Single(nodes[3].Children!);
    }

    [Fact]
    public void Nav_a_nodes_emit_items_and_are_stamped()
    {
        var pack = new NavStubPack();
        var outputs = pack.Handle(new ExperienceStep("p", "p", "start", new Dictionary<string, string>()));
        var tree = (UiWidgetTree)((UiSurface)outputs[0]).Props["tree"];
        var tabs = FindByType(tree, UiKitVocabulary.Tabs);
        Assert.Equal("p", tabs.Props["pack"]);
        var items = Assert.IsAssignableFrom<IReadOnlyList<object>>(tabs.Props["items"]);
        Assert.Equal(2, items.Count);
        var first = Assert.IsType<Dictionary<string, object?>>(items[0]);
        Assert.Equal("One", first["label"]);
        Assert.Equal("one", first["eventName"]);
    }

    private sealed class NavStubPack : KitExperience
    {
        protected override UiExperience Define() => Experience("p", "P")
            .Hop("start", s => s.Tabs(("One", "one"), ("Two", "two")))
            .Hop("one", s => s.Text("1")).Hop("two", s => s.Text("2"));
    }

    [Fact]
    public void Nav_b_nodes_emit_items()
    {
        var hop = new UiHop("h");
        hop.Sidebar(("Home", "home"), ("Settings", "settings")).BottomNav(("A", "a"), ("B", "b"));
        var nodes = hop.Factories.Select(f => f(new Dictionary<string, string>())).ToList();
        Assert.Equal(UiKitVocabulary.Sidebar, nodes[0].Type);
        Assert.Equal(UiKitVocabulary.BottomNav, nodes[1].Type);
        Assert.Equal(2, ((IReadOnlyList<object>)nodes[0].Props["items"]!).Count);
    }

    [Fact]
    public void Overlay_nodes_emit_open_flag_and_children()
    {
        var hop = new UiHop("h");
        hop.Dialog(true, "Confirm", d => d.Text("Sure?").Button("OK", "done")).Toast("Saved");
        var nodes = hop.Factories.Select(f => f(new Dictionary<string, string>())).ToList();
        Assert.Equal(UiKitVocabulary.Dialog, nodes[0].Type);
        Assert.Equal(true, nodes[0].Props["open"]);
        Assert.Equal("Confirm", nodes[0].Props["title"]);
        Assert.Equal(2, nodes[0].Children!.Count);
        Assert.Equal(UiKitVocabulary.Toast, nodes[1].Type);
        Assert.Equal("Saved", nodes[1].Props["message"]);
    }

    // UiGallery pack source test removed (demo literal deleted from Core).


    // ui-gallery (demo) seeds include test removed (literal deleted from Core).


    private static UiWidgetTree FindByType(UiWidgetTree node, string type)
    {
        if (node.Type == type)
        {
            return node;
        }

        foreach (var child in node.Children ?? [])
        {
            if (FindByType(child, type) is { } match)
            {
                return match;
            }
        }

        return null!;
    }
}
