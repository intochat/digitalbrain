using DigitalBrain.Core;

namespace DigitalBrain.Ui.Runtime;

using DigitalBrain.Ui.Contracts;

/// Simple browsable UiKit component gallery builder.
/// Emits a UiWidgetTree showcasing the main vocabulary items (server-driven).
/// Intended for use by Ino, a dedicated gallery experience, or marketplace ui-kit pack.
/// This is the foundation for the full "WinUI Gallery" style surface (Phase D).
public static class UiKitGallery
{
    public static UiWidgetTree Build(string title = "UiKit Gallery")
    {
        var items = new List<UiWidgetTree>
        {
            new(UiKitVocabulary.Heading, new Dictionary<string, object?> { ["text"] = title }),
            new(UiKitVocabulary.Text, new Dictionary<string, object?> { ["text"] = "Live demonstration of the official DigitalBrain UI kit (UiKitVocabulary + NeuronUiKit). Chat 'show gallery' or 'uikit gallery' to Ino to refresh." }),

            BuildDemo(UiKitVocabulary.Heading, "Heading", new() { ["text"] = "Section Heading" }),
            BuildDemo(UiKitVocabulary.Text, "Text", new() { ["text"] = "Sample text content from the kit." }),
            BuildDemo(UiKitVocabulary.TextField, "TextField", new() { ["label"] = "Name", ["value"] = "example" }),
            BuildDemo(UiKitVocabulary.Button, "Button", new() { ["label"] = "Click me" }),
            BuildDemo(UiKitVocabulary.Panel, "Panel", new() { ["title"] = "Panel title" }),
            BuildDemo(UiKitVocabulary.Checkbox, "Checkbox", new() { ["label"] = "Enabled", ["checked"] = true }),
            BuildDemo(UiKitVocabulary.Switch, "Switch", new() { ["label"] = "Active", ["value"] = false }),
            BuildDemo(UiKitVocabulary.Select, "Select", new() { ["label"] = "Choose", ["options"] = new[] { "One", "Two", "Three" } }),
            BuildDemo(UiKitVocabulary.List, "List", new() { ["items"] = new[] { "Item A", "Item B", "Item C" } }),
            BuildDemo(UiKitVocabulary.Tile, "Tile", new() { ["title"] = "Tile Title", ["subtitle"] = "Subtitle with actions" }),
            BuildDemo(UiKitVocabulary.Badge, "Badge", new() { ["text"] = "New" }),
            BuildDemo(UiKitVocabulary.Table, "Table", new() { ["columns"] = new[] { "Col1", "Col2" }, ["rows"] = new object[] { new[] { "a", "b" }, new[] { "c", "d" } } }),
            BuildDemo(UiKitVocabulary.GraphCanvas, "GraphCanvas", new() { ["title"] = "Sample graph" }),
            BuildDemo(UiKitVocabulary.Row, "Row layout", new() {  }),
            BuildDemo(UiKitVocabulary.Column, "Column layout", new() {  }),
            BuildDemo(UiKitVocabulary.Divider, "Divider", new() { }),
            BuildDemo(UiKitVocabulary.Avatar, "Avatar", new() { ["fallback"] = "IN" }),
            BuildDemo(UiKitVocabulary.Alert, "Alert", new() { ["title"] = "Info", ["description"] = "This is a kit alert." }),
            BuildDemo(UiKitVocabulary.Progress, "Progress", new() { ["value"] = 0.65 }),

            new(UiKitVocabulary.Gap, new Dictionary<string, object?>()),
            new(UiKitVocabulary.Text, new Dictionary<string, object?> { ["text"] = "All components are server-driven via UiWidgetTree. Use in packs with KitExperience or emit from neurons. Ino can generate more." }),
        };

        return new UiWidgetTree(UiKitVocabulary.Column, new Dictionary<string, object?>(), items);
    }

    private static UiWidgetTree BuildDemo(string type, string label, Dictionary<string, object?> extra)
    {
        extra["demoFor"] = type;
        return new(UiKitVocabulary.Panel, new Dictionary<string, object?> { ["title"] = label },
            new List<UiWidgetTree> { new(type, extra) });
    }
}