namespace DigitalBrain.Ui.Contracts;

using DigitalBrain.Core;
using System.Text.Json.Nodes;

[GenerateSerializer]
[Alias("DigitalBrain.Ui.Contracts.UiSurface")]
public record UiSurface(string Kind, IReadOnlyDictionary<string, object?> Props) : Synapse(nameof(UiSurface), DateTimeOffset.UtcNow)
{
    public const string RfwKind = "rfw";
    public const string WidgetTreeKind = "widget-tree";
    public const string AppShellKind = "app-shell";
    public const string ViewKind = "view";

    /// Creates a UiSurface carrying an RFW definition (unifies the previous separate RfwCard concept for UI purposes).
    public static UiSurface ForRfw(string libraryName, string rootWidget, string dataJson, string? source = null, string? emitter = null)
    {
        var props = new Dictionary<string, object?>
        {
            ["libraryName"] = libraryName,
            ["rootWidget"] = rootWidget,
            ["dataJson"] = dataJson
        };
        if (source is not null) props["source"] = source;
        if (emitter is not null) props[UiSurfaceKeys.Emitter] = emitter;

        return new UiSurface(RfwKind, props);
    }

    /// Creates an RFW hop surface tagged so an experience host can recognize it and pick the
    /// active hop. The marker is merged INTO dataJson (the RFW bridge forwards Props["dataJson"]
    /// verbatim, so a top-level prop alone would never reach the Flutter client).
    public static UiSurface ForExperienceHop(
        string pack,
        string experienceId,
        string surfaceId,
        string libraryName,
        string rootWidget,
        string dataJson,
        string? title = null,
        string? emitter = null)
    {
        var experienceRef = $"{pack}/{experienceId}";
        var payload = JsonNode.Parse(dataJson) as JsonObject ?? new JsonObject();
        payload["activeExperience"] = experienceRef;
        payload["experienceId"] = experienceId;
        payload["surfaceId"] = surfaceId;

        var props = new Dictionary<string, object?>
        {
            ["libraryName"] = libraryName,
            ["rootWidget"] = rootWidget,
            ["dataJson"] = payload.ToJsonString(),
            ["activeExperience"] = experienceRef,
            ["experienceId"] = experienceId,
            [UiSurfaceKeys.SurfaceId] = surfaceId,
        };
        if (title is not null) props[UiSurfaceKeys.Title] = title;
        if (emitter is not null) props[UiSurfaceKeys.Emitter] = emitter;

        return new UiSurface(RfwKind, props);
    }

    /// Creates a surface whose primary payload is a declarative widget tree (neurons author their own UI).
    /// The tree uses primitive names (e.g. "FSidebar", "FCard", "Panel") + children + bindings + actions.
    public static UiSurface ForWidgetTree(UiWidgetTree tree, string? title = null, string? emitter = null)
    {
        var props = new Dictionary<string, object?>
        {
            ["tree"] = tree
        };
        if (title is not null) props[UiSurfaceKeys.Title] = title;
        if (emitter is not null) props[UiSurfaceKeys.Emitter] = emitter;

        return new UiSurface(WidgetTreeKind, props);
    }

    // Typed-tree sibling of ForExperienceHop: an experience hop whose payload is a UiWidgetTree of ui:* nodes.
    // Markers live in Props; UiSurfaceRfwBridge merges them into the wire dataJson and keys correlation on surfaceId.
    public static UiSurface ForExperienceHopTree(
        string pack,
        string experienceId,
        string surfaceId,
        UiWidgetTree tree,
        string? title = null,
        string? emitter = null)
    {
        var props = new Dictionary<string, object?>
        {
            ["tree"] = tree,
            ["activeExperience"] = $"{pack}/{experienceId}",
            ["experienceId"] = experienceId,
            [UiSurfaceKeys.SurfaceId] = surfaceId,
        };
        if (title is not null) props[UiSurfaceKeys.Title] = title;
        if (emitter is not null) props[UiSurfaceKeys.Emitter] = emitter;
        return new UiSurface(WidgetTreeKind, props);
    }
}

/// Declarative widget tree emitted by neurons inside UiSurface (WidgetTreeKind).
/// Uses NeuronUiKit (neuron:*) and forui:* names (forui:FScaffold, forui:FAutocomplete, forui:FSidebar etc) + rfw escapes.
/// Renderer maps to ForUI widgets and sends events back as synapses. Client is thin host.
[GenerateSerializer]
[Alias("DigitalBrain.Ui.Contracts.UiWidgetTree")]
public record UiWidgetTree(
    [property: Id(0)] string Type, // "app-shell", NeuronUiKit.Menu, "forui:FScaffold", "forui:FAutocomplete", "list", "rfw", ...
    [property: Id(1)] IReadOnlyDictionary<string, object?> Props,
    [property: Id(2)] IReadOnlyList<UiWidgetTree>? Children = null,
    [property: Id(3)] string? RfwSource = null,
    [property: Id(4)] string? RfwRoot = null
);

/// Official Neuron UI Kit vocabulary (small, stable, server-driven only).
/// Neurons emit these as UiWidgetTree nodes inside app-shell / widget-tree surfaces.
/// Client renders; events carry targets or SynapseAction payloads back as UiInputSynapse.
public static class NeuronUiKit
{
    public const string Menu = "neuron:Menu";
    public const string MenuItem = "neuron:MenuItem";
    public const string ActionButton = "neuron:ActionButton";
    public const string NeuronButton = "neuron:NeuronButton";
    public const string NeuronList = "neuron:NeuronList";
    public const string NeuronListItem = "neuron:NeuronListItem";
    public const string Form = "neuron:Form";
    public const string Header = "neuron:Header";
    public const string Panel = "neuron:Panel";
    public const string Divider = "neuron:Divider";
    public const string Scaffold = "forui:FScaffold";
    public const string Sidebar = "forui:FSidebar";
    public const string Autocomplete = "forui:FAutocomplete";
    public const string TextField = "forui:FTextField";
    public const string Select = "forui:FSelect";
    public const string Notification = "forui:FNotification";
    public const string Toast = "forui:Toast";
    public const string Link = "ui:Link";

    // Self-explanatory helpers for common tree nodes (used by emitters; keep emission sites small).
    public static UiWidgetTree BuildMenuItem(string label, string? targetSurfaceKind = null, IReadOnlyDictionary<string, object?>? action = null)
    {
        var p = new Dictionary<string, object?> { ["label"] = label };
        if (targetSurfaceKind is not null) p["targetSurfaceKind"] = targetSurfaceKind;
        if (action is not null) p["action"] = action;
        return new UiWidgetTree(MenuItem, p);
    }

    public static UiWidgetTree BuildHeader(string title, string? subtitle = null)
    {
        var p = new Dictionary<string, object?> { ["title"] = title };
        if (subtitle is not null) p["subtitle"] = subtitle;
        return new UiWidgetTree(Header, p);
    }

    public static UiWidgetTree BuildMenu(string title, IReadOnlyList<UiWidgetTree> items)
        => new(Menu, new Dictionary<string, object?> { ["title"] = title }, items);

    public static UiWidgetTree BuildSidebar(string title, IReadOnlyList<UiWidgetTree> items)
        => new(Sidebar, new Dictionary<string, object?> { ["title"] = title }, items);

    public static UiWidgetTree BuildLink(string label, string url)
        => new(Link, new Dictionary<string, object?> { ["label"] = label, ["url"] = url });
}

// Curated UI-kit vocabulary (Slice 0). Each node is a thin ForUI cover on the client.
// Named UiKitVocabulary (not Ui) to avoid colliding with the DigitalBrain.Core.Ui namespace.
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
    {
        ["name"] = Name,
        ["type"] = Type,
        ["badge"] = Badge,
        ["description"] = Description,
        ["key"] = Key
    };
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
public record CanvasGraphGroup(
    [property: Id(0)] string Id,
    [property: Id(1)] string Label,
    [property: Id(2)] IReadOnlyDictionary<string, object?>? Details = null)
{
    public IReadOnlyDictionary<string, object?> ToProps() => new Dictionary<string, object?>
    {
        ["id"] = Id,
        ["label"] = Label,
        ["details"] = Details ?? new Dictionary<string, object?>()
    };
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

// Rich grammar-of-graphics spec for first-class interactive charts (maps directly to graphic package on client).
// Variables, marks, and selections are expressed as simple serializable structures.
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
    public const string Login = "login";
    public const string List = "list";
    public const string Ide = "ide";
    public const string ActivityGraph = "activity-graph";
    public const string TaskWindow = "task-window";
    public const string TaskManager = "task-manager";
    public const string UserInput = "user-input";
    public const string MarketplaceList = "marketplace-list";
    public const string InstalledBundles = "installed-bundles";
    public const string Workspace = "workspace";
    public const string Timeline = "timeline";
    public const string DataChart = "data-chart";
    public const string Table = "table";
    public const string GraphCanvas = "graph-canvas";
    // All UI is UiSurface based. These enable neurons to own chrome, nav and full main UI.
    public const string AppShell = "app-shell";        // main root chrome + nav + layout, streamed by a neuron
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
    public const string SynapseType = "synapseType";
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

public static class UiSurfaceActions
{
    public static IReadOnlyDictionary<string, object?> SynapseAction(
        string actionId,
        string label,
        string synapseType,
        IReadOnlyDictionary<string, object?>? props = null) => new Dictionary<string, object?>
        {
            [UiSurfaceKeys.ActionId] = actionId,
            [UiSurfaceKeys.Label] = label,
            [UiSurfaceKeys.SynapseType] = synapseType,
            [UiSurfaceKeys.Props] = props ?? new Dictionary<string, object?>()
        };
}

/// <summary>
/// Auth button surface. GmailDigest etc. return this so the UI kit knows to show Google icon + wire OAuth.
/// </summary>
[GenerateSerializer]
[Alias("DigitalBrain.Ui.Contracts.AuthButtonSurface")]
public record AuthButtonSurface(
    string Provider,
    string Label,
    string Icon = "default",
    string Action = "oauth"
) : UiSurface(UiSurfaceKinds.AuthButton, new Dictionary<string, object?>
{
    ["provider"] = Provider,
    ["label"] = Label,
    ["icon"] = Icon,
    ["action"] = Action
});

/// <summary>
/// Simple list surface for tasks / marketplace items etc.
/// </summary>
[GenerateSerializer]
[Alias("DigitalBrain.Ui.Contracts.ListSurface")]
public record ListSurface(
    string Title,
    IReadOnlyList<string> Items
) : UiSurface(UiSurfaceKinds.List, new Dictionary<string, object?>
{
    ["title"] = Title,
    ["items"] = Items
});

/// Dedicated surface for lightweight automations observability (reactions + scripts + last exec info).
/// Emitted by AutomationNeuron on register/remove/execute/query. Consumable by UI/HomeFeed.
[GenerateSerializer]
[Alias("DigitalBrain.Ui.Contracts.AutomationSurface")]
public record AutomationSurface(
    IReadOnlyList<ReactionView> Reactions,
    IReadOnlyList<ScriptView> Scripts,
    DateTimeOffset LastUpdated
) : UiSurface("automation", new Dictionary<string, object?>
{
    ["reactions"] = Reactions,
    ["scripts"] = Scripts,
    ["lastUpdated"] = LastUpdated
});

[GenerateSerializer]
[Alias("DigitalBrain.Ui.Contracts.ReactionView")]
public record ReactionView(
    [property: Id(0)] string Id,
    [property: Id(1)] string When,
    [property: Id(2)] string ScriptRef,
    [property: Id(3)] string? Target = null,
    [property: Id(4)] int ExecCount = 0
);

[GenerateSerializer]
[Alias("DigitalBrain.Ui.Contracts.ScriptView")]
public record ScriptView(
    [property: Id(0)] string Id,
    [property: Id(1)] string Description,
    [property: Id(2)] string CodePreview,
    [property: Id(3)] int UsageCount = 0
);

/// Data-only visual graph foundation for automations (priority 7). Nodes = reactions/scripts, edges capture when/then.
/// A future editor emits the same RegisterScript/RegisterReaction records; this is just observable surface.
/// Rfw / Flutter can render from the nodes/edges.
[GenerateSerializer]
[Alias("DigitalBrain.Ui.Contracts.AutomationGraphSurface")]
public record AutomationGraphSurface(
    [property: Id(0)] string Title,
    [property: Id(1)] IReadOnlyList<AutomationGraphNode> Nodes,
    [property: Id(2)] IReadOnlyList<AutomationGraphEdge> Edges,
    [property: Id(3)] DateTimeOffset GeneratedAt
) : UiSurface("automation-graph", new Dictionary<string, object?>
{
    ["title"] = Title,
    ["nodes"] = Nodes,
    ["edges"] = Edges,
    ["generatedAt"] = GeneratedAt
});

[GenerateSerializer]
[Alias("DigitalBrain.Ui.Contracts.AutomationGraphNode")]
public record AutomationGraphNode(
    [property: Id(0)] string Id,
    [property: Id(1)] string Kind, // "reaction" | "script"
    [property: Id(2)] string Label,
    [property: Id(3)] IReadOnlyDictionary<string, object?>? Props = null
);

[GenerateSerializer]
[Alias("DigitalBrain.Ui.Contracts.AutomationGraphEdge")]
public record AutomationGraphEdge(
    [property: Id(0)] string From,
    [property: Id(1)] string To,
    [property: Id(2)] string Label // e.g. "when" or "uses"
);

/// <summary>
/// Tabular data surface rendered by the client as a rich UI kit table (used for dropped Excel/CSV in chat).
/// Columns and rows are string data for simple, self-explanatory rendering.
/// </summary>
[GenerateSerializer]
[Alias("DigitalBrain.Ui.Contracts.TableSurface")]
public record TableSurface(
    string Title,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string>> Rows
) : UiSurface(UiSurfaceKinds.Table, new Dictionary<string, object?>
{
    ["title"] = Title,
    ["columns"] = Columns,
    ["rows"] = Rows
});

/// <summary>
/// IDE / code edit surface for live INO modification + execute.
/// </summary>
[GenerateSerializer]
[Alias("DigitalBrain.Ui.Contracts.IdeSurface")]
public record IdeSurface(
    string Title,
    string InitialCode,
    string Language = "ino"
) : UiSurface(UiSurfaceKinds.Ide, new Dictionary<string, object?>
{
    ["title"] = Title,
    ["code"] = InitialCode,
    ["language"] = Language
});

// Kernel-owned surface kinds centralized here with the other UI contract names.
// Kernel surfaces remain versioned with the kernel pack but names are single-sourced in UI contracts.
public static class KernelUiSurfaceKinds
{
    public const string Dashboard = "kernel-dashboard";
    public const string Rolling = "kernel-rolling";
    public const string RollingDrain = "kernel-rolling-drain";
    public const string RollingVerify = "kernel-rolling-verify";
    public const string RollingComplete = "kernel-rolling-complete";
    public const string RollingRollback = "kernel-rolling-rollback";
}
