using DigitalBrain.Core;

namespace DigitalBrain.Ui.Runtime;

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
            new(UiKitVocabulary.Text, new Dictionary<string, object?> { ["text"] = "Demonstration of core components from UiKitVocabulary." }),

            BuildDemo(UiKitVocabulary.Text, "Text", new() { ["text"] = "Sample text content" }),
            BuildDemo(UiKitVocabulary.TextField, "TextField", new() { ["label"] = "Name", ["value"] = "example" }),
            BuildDemo(UiKitVocabulary.Button, "Button", new() { ["label"] = "Click me" }),
            BuildDemo(UiKitVocabulary.Panel, "Panel", new() { ["title"] = "Panel title" }),
            BuildDemo(UiKitVocabulary.Checkbox, "Checkbox", new() { ["label"] = "Enabled", ["checked"] = true }),
            BuildDemo(UiKitVocabulary.Switch, "Switch", new() { ["label"] = "Active", ["value"] = false }),
            BuildDemo(UiKitVocabulary.Select, "Select", new() { ["label"] = "Choose", ["options"] = new[] { "One", "Two" } }),
            BuildDemo(UiKitVocabulary.List, "List", new() { ["items"] = new[] { "Item A", "Item B" } }),
            BuildDemo(UiKitVocabulary.Table, "Table", new() { ["columns"] = new[] { "Col1", "Col2" }, ["rows"] = new object[] { new[] { "a", "b" } } }),
            BuildDemo(UiKitVocabulary.GraphCanvas, "Graph", new() { ["title"] = "Sample graph" }),

            new(UiKitVocabulary.Gap, new Dictionary<string, object?>()),
            new(UiKitVocabulary.Text, new Dictionary<string, object?> { ["text"] = "More components (Row/Column, Dialog, Toast, etc.) follow the same UiWidgetTree pattern." }),
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