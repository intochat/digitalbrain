using DigitalBrain.Core;

namespace DigitalBrain.Pack.Contracts.UiKit;

using DigitalBrain.Ui.Contracts;

// Author-facing fluent definition of an experience: an ordered set of named hops, the first of which is the entry.
public sealed class UiExperience
{
    public string Id { get; }
    public string Name { get; }
    internal List<UiHop> Hops { get; } = new();

    internal UiExperience(string id, string name)
    {
        Id = id;
        Name = name;
    }

    public UiExperience Hop(string hopId, Action<UiHop> body)
    {
        var hop = new UiHop(hopId);
        body(hop);
        Hops.Add(hop);
        return this;
    }
}

// One hop: an ordered list of node factories. A factory may depend on the accumulated flow state
// (e.g. the greeting text uses the captured name), so the literal is computed at emit time, keeping the client dumb.
public sealed class UiHop
{
    public string Id { get; }
    internal List<Func<IReadOnlyDictionary<string, string>, UiWidgetTree>> Factories { get; } = new();

    internal UiHop(string id) => Id = id;

    public UiHop Text(string text)
    {
        Factories.Add(_ => new UiWidgetTree(UiKitVocabulary.Text, new Dictionary<string, object?> { ["text"] = text }));
        return this;
    }

    public UiHop Text(Func<IReadOnlyDictionary<string, string>, string> text)
    {
        Factories.Add(state => new UiWidgetTree(UiKitVocabulary.Text, new Dictionary<string, object?> { ["text"] = text(state) }));
        return this;
    }

    public UiHop TextField(string name, string placeholder = "")
    {
        Factories.Add(_ => new UiWidgetTree(UiKitVocabulary.TextField,
            new Dictionary<string, object?> { ["name"] = name, ["placeholder"] = placeholder }));
        return this;
    }

    public UiHop Button(string label, string goTo)
    {
        Factories.Add(_ => new UiWidgetTree(UiKitVocabulary.Button,
            new Dictionary<string, object?> { ["label"] = label, ["eventName"] = goTo }));
        return this;
    }

    public UiHop Panel(Action<UiHop> body)
    {
        var inner = new UiHop(Id);
        body(inner);
        Factories.Add(state => new UiWidgetTree(UiKitVocabulary.Panel, new Dictionary<string, object?>(),
            inner.Factories.Select(factory => factory(state)).ToList()));
        return this;
    }

    public UiHop Checkbox(string name, string label)
    {
        Factories.Add(_ => new UiWidgetTree(UiKitVocabulary.Checkbox,
            new Dictionary<string, object?> { ["name"] = name, ["label"] = label }));
        return this;
    }

    public UiHop Switch(string name, string label)
    {
        Factories.Add(_ => new UiWidgetTree(UiKitVocabulary.Switch,
            new Dictionary<string, object?> { ["name"] = name, ["label"] = label }));
        return this;
    }

    public UiHop TextArea(string name, string placeholder = "")
    {
        Factories.Add(_ => new UiWidgetTree(UiKitVocabulary.TextArea,
            new Dictionary<string, object?> { ["name"] = name, ["placeholder"] = placeholder }));
        return this;
    }

    public UiHop Select(string name, IReadOnlyList<string> options, string? label = null)
    {
        Factories.Add(_ => new UiWidgetTree(UiKitVocabulary.Select, new Dictionary<string, object?>
        {
            ["name"] = name,
            ["options"] = options,
            ["label"] = label ?? string.Empty
        }));
        return this;
    }

    public UiHop RadioGroup(string name, IReadOnlyList<string> options, string? label = null)
    {
        Factories.Add(_ => new UiWidgetTree(UiKitVocabulary.RadioGroup, new Dictionary<string, object?>
        {
            ["name"] = name,
            ["options"] = options,
            ["label"] = label ?? string.Empty
        }));
        return this;
    }

    public UiHop Slider(string name, double min = 0, double max = 1, string? label = null)
    {
        Factories.Add(_ => new UiWidgetTree(UiKitVocabulary.Slider, new Dictionary<string, object?>
        {
            ["name"] = name,
            ["min"] = min,
            ["max"] = max,
            ["label"] = label ?? string.Empty
        }));
        return this;
    }

    public UiHop DateField(string name, string? label = null)
    {
        Factories.Add(_ => new UiWidgetTree(UiKitVocabulary.DateField, new Dictionary<string, object?>
        {
            ["name"] = name,
            ["label"] = label ?? string.Empty
        }));
        return this;
    }

    public UiHop Row(Action<UiHop> body)
    {
        var inner = new UiHop(Id);
        body(inner);
        Factories.Add(state => new UiWidgetTree(UiKitVocabulary.Row, new Dictionary<string, object?>(),
            inner.Factories.Select(f => f(state)).ToList()));
        return this;
    }

    public UiHop Column(Action<UiHop> body)
    {
        var inner = new UiHop(Id);
        body(inner);
        Factories.Add(state => new UiWidgetTree(UiKitVocabulary.Column, new Dictionary<string, object?>(),
            inner.Factories.Select(f => f(state)).ToList()));
        return this;
    }

    public UiHop Divider()
    {
        Factories.Add(_ => new UiWidgetTree(UiKitVocabulary.Divider, new Dictionary<string, object?>()));
        return this;
    }

    public UiHop Header(string title)
    {
        Factories.Add(_ => new UiWidgetTree(UiKitVocabulary.Header, new Dictionary<string, object?> { ["title"] = title }));
        return this;
    }

    public UiHop Gap(double size = 16)
    {
        Factories.Add(_ => new UiWidgetTree(UiKitVocabulary.Gap, new Dictionary<string, object?> { ["size"] = size }));
        return this;
    }

    public UiHop Heading(string text)
    {
        Factories.Add(_ => new UiWidgetTree(UiKitVocabulary.Heading, new Dictionary<string, object?> { ["text"] = text }));
        return this;
    }

    public UiHop Icon(string name)
    {
        Factories.Add(_ => new UiWidgetTree(UiKitVocabulary.Icon, new Dictionary<string, object?> { ["name"] = name }));
        return this;
    }

    public UiHop Avatar(string? imageUrl = null, string? fallback = null)
    {
        Factories.Add(_ => new UiWidgetTree(UiKitVocabulary.Avatar, new Dictionary<string, object?>
        {
            ["imageUrl"] = imageUrl ?? string.Empty,
            ["fallback"] = fallback ?? string.Empty
        }));
        return this;
    }

    public UiHop Badge(string text)
    {
        Factories.Add(_ => new UiWidgetTree(UiKitVocabulary.Badge, new Dictionary<string, object?> { ["text"] = text }));
        return this;
    }

    public UiHop Tile(string title, string? subtitle = null, string? goTo = null)
    {
        Factories.Add(_ =>
        {
            var props = new Dictionary<string, object?> { ["title"] = title, ["subtitle"] = subtitle ?? string.Empty };
            if (goTo is not null) props["eventName"] = goTo;
            return new UiWidgetTree(UiKitVocabulary.Tile, props);
        });
        return this;
    }

    public UiHop List(Action<UiHop> body)
    {
        var inner = new UiHop(Id);
        body(inner);
        Factories.Add(state => new UiWidgetTree(UiKitVocabulary.List, new Dictionary<string, object?>(),
            inner.Factories.Select(f => f(state)).ToList()));
        return this;
    }

    public UiHop Alert(string title, string? subtitle = null)
    {
        Factories.Add(_ => new UiWidgetTree(UiKitVocabulary.Alert, new Dictionary<string, object?>
        {
            ["title"] = title,
            ["subtitle"] = subtitle ?? string.Empty
        }));
        return this;
    }

    public UiHop Progress(double value)
    {
        Factories.Add(_ => new UiWidgetTree(UiKitVocabulary.Progress, new Dictionary<string, object?> { ["value"] = value }));
        return this;
    }

    public UiHop Spinner()
    {
        Factories.Add(_ => new UiWidgetTree(UiKitVocabulary.Spinner, new Dictionary<string, object?>()));
        return this;
    }

    public UiHop Tooltip(string tip, Action<UiHop> body)
    {
        var inner = new UiHop(Id);
        body(inner);
        Factories.Add(state => new UiWidgetTree(UiKitVocabulary.Tooltip,
            new Dictionary<string, object?> { ["tip"] = tip },
            inner.Factories.Select(f => f(state)).ToList()));
        return this;
    }

    public UiHop Tabs(params (string label, string goTo)[] items)
    {
        Factories.Add(_ => new UiWidgetTree(UiKitVocabulary.Tabs, new Dictionary<string, object?> { ["items"] = NavItems(items) }));
        return this;
    }

    public UiHop Breadcrumb(params (string label, string goTo)[] items)
    {
        Factories.Add(_ => new UiWidgetTree(UiKitVocabulary.Breadcrumb, new Dictionary<string, object?> { ["items"] = NavItems(items) }));
        return this;
    }

    public UiHop Sidebar(params (string label, string goTo)[] items)
    {
        Factories.Add(_ => new UiWidgetTree(UiKitVocabulary.Sidebar, new Dictionary<string, object?> { ["items"] = NavItems(items) }));
        return this;
    }

    public UiHop BottomNav(params (string label, string goTo)[] items)
    {
        Factories.Add(_ => new UiWidgetTree(UiKitVocabulary.BottomNav, new Dictionary<string, object?> { ["items"] = NavItems(items) }));
        return this;
    }

    public UiHop Pagination(int pages, string goToPrefix)
    {
        var items = Enumerable.Range(0, pages)
            .Select(i => new Dictionary<string, object?> { ["label"] = (i + 1).ToString(), ["eventName"] = goToPrefix + i })
            .ToList();
        Factories.Add(_ => new UiWidgetTree(UiKitVocabulary.Pagination, new Dictionary<string, object?> { ["items"] = items }));
        return this;
    }

    public UiHop Dialog(bool open, string title, Action<UiHop> body)
    {
        var inner = new UiHop(Id);
        body(inner);
        Factories.Add(state => new UiWidgetTree(UiKitVocabulary.Dialog,
            new Dictionary<string, object?> { ["open"] = open, ["title"] = title },
            inner.Factories.Select(f => f(state)).ToList()));
        return this;
    }

    public UiHop Sheet(bool open, string title, Action<UiHop> body)
    {
        var inner = new UiHop(Id);
        body(inner);
        Factories.Add(state => new UiWidgetTree(UiKitVocabulary.Sheet,
            new Dictionary<string, object?> { ["open"] = open, ["title"] = title },
            inner.Factories.Select(f => f(state)).ToList()));
        return this;
    }

    public UiHop Toast(string message)
    {
        Factories.Add(_ => new UiWidgetTree(UiKitVocabulary.Toast, new Dictionary<string, object?> { ["message"] = message }));
        return this;
    }

    private static List<Dictionary<string, object?>> NavItems((string label, string goTo)[] items) =>
        items.Select(i => new Dictionary<string, object?> { ["label"] = i.label, ["eventName"] = i.goTo }).ToList();
}
