using DigitalBrain.Core;
using DigitalBrain.Flutter.Chat;
using DigitalBrain.Flutter.Layout;
using DigitalBrain.Flutter.Surface;
using DigitalBrain.Flutter.Text;
using DigitalBrain.Flutter.VoiceInput;

namespace DigitalBrain.Flutter;

// A node names a part of the app; started for a key, it becomes the neuron "{key}/{part}".
public sealed record UiNode(string Kind, string Part, Func<IGrainFactory, string, Task> Apply, IReadOnlyList<UiNode> Children);

public sealed class UiComposer
{
    public static string NameOf(string key, string part) => key + "/" + part;

    public UiNode Surface(string title, params UiNode[] children) => Surface("surface", title, children);

    public UiNode Surface(string part, string title, params UiNode[] children) => new(UIVocabulary.SurfaceType, part, async (grains, key) =>
    {
        var surface = grains.GetGrain<ISurface>(NameOf(key, part));
        await surface.Set(new(title, References(key, children)), (await surface.Read()).Revision);
    }, children);

    public UiNode Column(params UiNode[] children) => Layout("layout", "column", children);

    public UiNode Layout(string part, string mode, params UiNode[] children) => new(UIVocabulary.LayoutType, part, async (grains, key) =>
    {
        var layout = grains.GetGrain<ILayout>(NameOf(key, part));
        await layout.Set(new(mode, References(key, children)), (await layout.Read()).Revision);
    }, children);

    public UiNode Text(string part, string markdown) =>
        new(UIVocabulary.TextType, part, (grains, key) => grains.GetGrain<IText>(NameOf(key, part)).Set(markdown), []);

    public UiNode Chat(string part, string label) =>
        new(UIVocabulary.ChatType, part, (grains, key) => grains.GetGrain<IChat>(NameOf(key, part)).Configure(label), []);

    public UiNode VoiceInput(string part, string label) =>
        new(UIVocabulary.VoiceInputType, part, (grains, key) => grains.GetGrain<IVoiceInput>(NameOf(key, part)).Configure(label), []);

    internal static async Task Compose(UiNode node, ApplicationStart start)
    {
        // Children first, so a container never refers to a node that is not configured yet.
        foreach (var child in node.Children) { await Compose(child, start); }
        await node.Apply(start.Grains, start.Key);
    }

    private static IReadOnlyList<UiChildRef> References(string key, UiNode[] children) =>
        [.. children.Select(child => new UiChildRef(child.Kind, NameOf(key, child.Part)))];
}

public static class UiAppBuilderExtensions
{
    public static IAppBuilder Ui(this IAppBuilder app, Func<UiComposer, UiNode> compose)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(compose);
        var root = compose(new UiComposer());
        return app.OnStart(start => UiComposer.Compose(root, start));
    }
}
