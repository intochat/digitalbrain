namespace DigitalBrain.Ui.Contracts;

[GenerateSerializer]
[Alias("DigitalBrain.Ui.Contracts.UiSurface")]
public record UiSurface(string Kind, IReadOnlyDictionary<string, object?> Props)
{
    public const string RfwKind = "rfw";
    public const string WidgetTreeKind = "widget-tree";
    public const string AppShellKind = "app-shell";
    public const string ViewKind = "view";
    public static UiSurface ForRfw(string libraryName, string rootWidget, string dataJson, string? source = null, string? emitter = null)
    {
        var props = new Dictionary<string, object?> { ["libraryName"] = libraryName, ["rootWidget"] = rootWidget, ["dataJson"] = dataJson };
        if (source is not null)
        {
            props["source"] = source;
        }
        if (emitter is not null)
        {
            props[UiSurfaceKeys.Emitter] = emitter;
        }
        return new UiSurface(RfwKind, props);
    }
    public static UiSurface ForWidgetTree(UiWidgetTree tree, string? title = null, string? emitter = null)
    {
        var props = new Dictionary<string, object?> { ["tree"] = tree };
        if (title is not null)
        {
            props[UiSurfaceKeys.Title] = title;
        }
        if (emitter is not null)
        {
            props[UiSurfaceKeys.Emitter] = emitter;
        }
        return new UiSurface(WidgetTreeKind, props);
    }
}
[GenerateSerializer]
[Alias("DigitalBrain.Ui.Contracts.UiWidgetTree")]
public record UiWidgetTree(
    [property: Id(0)] string Type,
    [property: Id(1)] IReadOnlyDictionary<string, object?> Props,
    [property: Id(2)] IReadOnlyList<UiWidgetTree>? Children = null,
    [property: Id(3)] string? RfwSource = null,
    [property: Id(4)] string? RfwRoot = null
);
public static class UiKitVocabulary
{
    public const string Screen = "ui:Screen";
    public const string Text = "ui:Text";
    public const string TextField = "ui:TextField";
    public const string Button = "ui:Button";
    public const string Panel = "ui:Panel";
    public const string Checkbox = "ui:Checkbox";
    public const string Switch = "ui:Switch";
    public const string TextArea = "ui:TextArea";
    public const string Select = "ui:Select";
    public const string RadioGroup = "ui:RadioGroup";
    public const string Slider = "ui:Slider";
    public const string DateField = "ui:DateField";
    public const string Row = "ui:Row";
    public const string Column = "ui:Column";
    public const string Divider = "ui:Divider";
    public const string Header = "ui:Header";
    public const string Gap = "ui:Gap";
    public const string Heading = "ui:Heading";
    public const string Icon = "ui:Icon";
    public const string Avatar = "ui:Avatar";
    public const string Badge = "ui:Badge";
    public const string Tile = "ui:Tile";
    public const string List = "ui:List";
    public const string Tabs = "ui:Tabs";
    public const string Breadcrumb = "ui:Breadcrumb";
    public const string Pagination = "ui:Pagination";
    public const string Alert = "ui:Alert";
    public const string Progress = "ui:Progress";
    public const string Spinner = "ui:Spinner";
    public const string Tooltip = "ui:Tooltip";
    public const string Sidebar = "ui:Sidebar";
    public const string BottomNav = "ui:BottomNav";
    public const string Dialog = "ui:Dialog";
    public const string Sheet = "ui:Sheet";
    public const string Toast = "ui:Toast";
    public const string Table = "ui:Table";
    public const string GraphCanvas = "ui:GraphCanvas";
    public const string Link = "ui:Link";
}
[GenerateSerializer]
[Alias("DigitalBrain.Ui.Contracts.CanvasGraphSpec")]
public record CanvasGraphSpec(
    [property: Id(0)] string Title,
    [property: Id(1)] IReadOnlyList<CanvasGraphNode> Nodes,
    [property: Id(2)] IReadOnlyList<CanvasGraphEdge> Edges,
    [property: Id(3)] string Layout = "force",
    [property: Id(4)] IReadOnlyList<CanvasGraphGroup>? Groups = null,
    [property: Id(5)] string? Summary = null)
{
    public IReadOnlyDictionary<string, object?> ToProps() => new Dictionary<string, object?>
    {
        ["title"] = Title,
        ["layout"] = Layout,
        ["nodes"] = Nodes.Select(n => n.ToProps()).ToArray(),
        ["edges"] = Edges.Select(e => e.ToProps()).ToArray(),
        ["groups"] = Groups?.Select(g => g.ToProps()).ToArray() ?? Array.Empty<IReadOnlyDictionary<string, object?>>(),
        ["summary"] = Summary
    };
}
[GenerateSerializer]
[Alias("DigitalBrain.Ui.Contracts.CanvasGraphNode")]
public record CanvasGraphNode(
    [property: Id(0)] string Id,
    [property: Id(1)] string Label,
    [property: Id(2)] string? Kind = null,
    [property: Id(3)] string? Group = null,
    [property: Id(4)] IReadOnlyList<CanvasGraphField>? Fields = null,
    [property: Id(5)] IReadOnlyDictionary<string, object?>? Details = null)
{
    public IReadOnlyDictionary<string, object?> ToProps() => new Dictionary<string, object?>
    {
        ["id"] = Id,
        ["label"] = Label,
        ["kind"] = Kind,
        ["group"] = Group,
        ["fields"] = Fields?.Select(f => f.ToProps()).ToArray() ?? Array.Empty<IReadOnlyDictionary<string, object?>>(),
        ["details"] = Details ?? new Dictionary<string, object?>()
    };
}
[GenerateSerializer]
[Alias("DigitalBrain.Ui.Contracts.CanvasGraphField")]
public record CanvasGraphField(
    [property: Id(0)] string Name,
    [property: Id(1)] string? Type = null,
    [property: Id(2)] string? Badge = null,
    [property: Id(3)] string? Description = null,
    [property: Id(4)] bool Key = false)
{
    public IReadOnlyDictionary<string, object?> ToProps() => new Dictionary<string, object?>
    { ["name"] = Name, ["type"] = Type, ["badge"] = Badge, ["description"] = Description, ["key"] = Key };
}
[GenerateSerializer]
[Alias("DigitalBrain.Ui.Contracts.CanvasGraphEdge")]
public record CanvasGraphEdge(
    [property: Id(0)] string Id,
    [property: Id(1)] string From,
    [property: Id(2)] string To,
    [property: Id(3)] string? Label = null,
    [property: Id(4)] string? Kind = null,
    [property: Id(5)] IReadOnlyDictionary<string, object?>? Details = null)
{
    public IReadOnlyDictionary<string, object?> ToProps() => new Dictionary<string, object?>
    {
        ["id"] = Id,
        ["from"] = From,
        ["to"] = To,
        ["label"] = Label,
        ["kind"] = Kind,
        ["details"] = Details ?? new Dictionary<string, object?>()
    };
}
[GenerateSerializer]
[Alias("DigitalBrain.Ui.Contracts.CanvasGraphGroup")]
public record CanvasGraphGroup([property: Id(0)] string Id, [property: Id(1)] string Label, [property: Id(2)] IReadOnlyDictionary<string, object?>? Details = null)
{
    public IReadOnlyDictionary<string, object?> ToProps() => new Dictionary<string, object?> { ["id"] = Id, ["label"] = Label, ["details"] = Details ?? new Dictionary<string, object?>() };
}
[GenerateSerializer]
[Alias("DigitalBrain.Ui.Contracts.ChartSpec")]
public record ChartSpec(
    [property: Id(0)] string Title,
    [property: Id(1)] string ChartType,
    [property: Id(2)] IReadOnlyList<IReadOnlyDictionary<string, object?>> Data,
    [property: Id(3)] string X,
    [property: Id(4)] string Y,
    [property: Id(5)] string? Series = null,
    [property: Id(6)] string? Color = null,
    [property: Id(7)] bool Tooltip = true,
    [property: Id(8)] bool Crosshair = true,
    [property: Id(9)] string? Summary = null)
{
    public IReadOnlyDictionary<string, object?> ToProps() => new Dictionary<string, object?>
    {
        ["title"] = Title,
        ["chartType"] = ChartType,
        ["data"] = Data,
        ["x"] = X,
        ["y"] = Y,
        ["series"] = Series,
        ["color"] = Color,
        ["tooltip"] = Tooltip,
        ["crosshair"] = Crosshair,
        ["summary"] = Summary
    };
}
[GenerateSerializer]
[Alias("DigitalBrain.Ui.Contracts.GraphicSpec")]
public record GraphicSpec(
    [property: Id(0)] string Title,
    [property: Id(1)] IReadOnlyList<IReadOnlyDictionary<string, object?>> Data,
    [property: Id(2)] IReadOnlyDictionary<string, object?> Variables,
    [property: Id(3)] IReadOnlyList<IReadOnlyDictionary<string, object?>> Marks,
    [property: Id(4)] IReadOnlyDictionary<string, object?>? Selections = null,
    [property: Id(5)] string? Summary = null,
    [property: Id(6)] IReadOnlyDictionary<string, object?>? Annotations = null)
{
    public IReadOnlyDictionary<string, object?> ToProps() => new Dictionary<string, object?>
    {
        ["title"] = Title,
        ["data"] = Data,
        ["variables"] = Variables,
        ["marks"] = Marks,
        ["selections"] = Selections,
        ["summary"] = Summary,
        ["annotations"] = Annotations
    };
}
public static class UiSurfaceKinds
{
    public const string AuthButton = "auth-button";
    public const string List = "list";
    public const string ActivityGraph = "activity-graph";
    public const string TaskWindow = "task-window";
    public const string TaskManager = "task-manager";
    public const string UserInput = "user-input";
    public const string Workspace = "workspace";
    public const string Timeline = "timeline";
    public const string DataChart = "data-chart";
    public const string Table = "table";
    public const string GraphCanvas = "graph-canvas";
    public const string AppShell = "app-shell";
    public const string ShellChrome = "shell-chrome";
    public const string NavConfig = "nav-config";
    public const string ViewDefinition = "view-definition";
}
public static class UiSurfaceKeys
{
    public const string SurfaceId = "surfaceId";
    public const string Emitter = "emitter";
    public const string Title = "title";
    public const string Priority = "priority";
    public const string RequiresInput = "requiresInput";
    public const string Actions = "actions";
    public const string Layout = "layout";
    public const string ActionId = "actionId";
    public const string Label = "label";
    public const string Props = "props";
    public const string ChartSpec = "chartSpec";
    public const string GraphSpec = "graphSpec";
}
public static class UiSurfaceLayouts
{
    public const string Panel = "panel";
    public const string Inline = "inline";
    public const string Drawer = "drawer";
    public const string Modal = "modal";
    public const string Compact = "compact";
}
[GenerateSerializer]
[Alias("DigitalBrain.Ui.Contracts.AuthButtonSurface")]
public record AuthButtonSurface(string Provider, string Label, string Icon = "default", string Action = "oauth") : UiSurface(UiSurfaceKinds.AuthButton, new Dictionary<string, object?> { ["provider"] = Provider, ["label"] = Label, ["icon"] = Icon, ["action"] = Action });
[GenerateSerializer]
[Alias("DigitalBrain.Ui.Contracts.ListSurface")]
public record ListSurface(string Title, IReadOnlyList<string> Items) : UiSurface(UiSurfaceKinds.List, new Dictionary<string, object?> { ["title"] = Title, ["items"] = Items });
[GenerateSerializer]
[Alias("DigitalBrain.Ui.Contracts.TableSurface")]
public record TableSurface(string Title, IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows) : UiSurface(UiSurfaceKinds.Table, new Dictionary<string, object?> { ["title"] = Title, ["columns"] = Columns, ["rows"] = Rows });
