using System.Text.Json.Serialization;

namespace DigitalBrain.UI;

/// <summary>A screen an app returns: a title and a tree of nodes. Renderer-neutral.</summary>
public sealed record View(string Title, Node Root);

/// <summary>A node of a view.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Column), "column")]
[JsonDerivedType(typeof(Row), "row")]
[JsonDerivedType(typeof(Grid), "grid")]
[JsonDerivedType(typeof(SplitPane), "splitPane")]
[JsonDerivedType(typeof(Surface), "surface")]
[JsonDerivedType(typeof(Tabs), "tabs")]
[JsonDerivedType(typeof(Divider), "divider")]
[JsonDerivedType(typeof(Spacer), "spacer")]
[JsonDerivedType(typeof(Collection), "collection")]
[JsonDerivedType(typeof(Heading), "heading")]
[JsonDerivedType(typeof(Text), "text")]
[JsonDerivedType(typeof(Badge), "badge")]
[JsonDerivedType(typeof(TagPill), "tagPill")]
[JsonDerivedType(typeof(TextField), "textField")]
[JsonDerivedType(typeof(Switch), "switch")]
[JsonDerivedType(typeof(Select), "select")]
[JsonDerivedType(typeof(Progress), "progress")]
[JsonDerivedType(typeof(InfoBar), "infoBar")]
[JsonDerivedType(typeof(Toast), "toast")]
[JsonDerivedType(typeof(Card), "card")]
[JsonDerivedType(typeof(Button), "button")]
[JsonDerivedType(typeof(DataTable), "dataTable")]
[JsonDerivedType(typeof(Chart), "chart")]
[JsonDerivedType(typeof(ApprovalCardNode), "approvalCard")]
[JsonDerivedType(typeof(ChoiceCardNode), "choiceCard")]
[JsonDerivedType(typeof(DraftCardNode), "draftCard")]
[JsonDerivedType(typeof(RecordCardNode), "recordCard")]
[JsonDerivedType(typeof(SecretCardNode), "secretCard")]
[JsonDerivedType(typeof(JobCardNode), "jobCard")]
[JsonDerivedType(typeof(BrainSceneNode), "brainScene")]
[JsonDerivedType(typeof(NeuronRef), "neuronRef")]
[JsonDerivedType(typeof(AppShellNode), "appShell")]
[JsonDerivedType(typeof(NavItemNode), "navItem")]
[JsonDerivedType(typeof(StatCardNode), "statCard")]
public abstract record Node;

/// <summary>Children laid out top to bottom.</summary>
public sealed record Column(IReadOnlyList<Node> Children) : Node
{
    /// <summary>Vertical spacing between children in logical pixels.</summary>
    public double Gap { get; init; } = 12;

    /// <summary>Optional inner padding in logical pixels.</summary>
    public double Padding { get; init; }

    /// <summary>Optional fixed/flex extents per child.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<double>? Extents { get; init; }

    /// <inheritdoc />
    public bool Equals(Column? other) => other is not null && Children.SequenceEqual(other.Children) && Gap.Equals(other.Gap) && Padding.Equals(other.Padding) && ((Extents is null && other.Extents is null) || (Extents is not null && other.Extents is not null && Extents.SequenceEqual(other.Extents)));

    /// <inheritdoc />
    public override int GetHashCode() => Children.Count;
}

/// <summary>Children laid out left to right.</summary>
public sealed record Row(IReadOnlyList<Node> Children) : Node
{
    /// <summary>Horizontal spacing between children in logical pixels.</summary>
    public double Gap { get; init; } = 12;

    /// <summary>Optional inner padding in logical pixels.</summary>
    public double Padding { get; init; }

    /// <summary>Optional fixed/flex extents per child.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<double>? Extents { get; init; }

    /// <inheritdoc />
    public bool Equals(Row? other) => other is not null && Children.SequenceEqual(other.Children) && Gap.Equals(other.Gap) && Padding.Equals(other.Padding) && ((Extents is null && other.Extents is null) || (Extents is not null && other.Extents is not null && Extents.SequenceEqual(other.Extents)));

    /// <inheritdoc />
    public override int GetHashCode() => Children.Count;
}

/// <summary>Children arranged in a multi-column grid.</summary>
public sealed record Grid(int Columns, IReadOnlyList<Node> Children, double Gap = 12) : Node
{
    /// <inheritdoc />
    public bool Equals(Grid? other) => other is not null && Columns == other.Columns && Gap.Equals(other.Gap) && Children.SequenceEqual(other.Children);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Columns, Children.Count);
}

/// <summary>Two-pane desktop split view.</summary>
public sealed record SplitPane(Node Leading, Node Trailing, double LeadingExtent = 280, double Gap = 16) : Node;

/// <summary>Known surface materiality kinds.</summary>
public static class SurfaceKind
{
    /// <summary>Frosted glass / acrylic surface.</summary>
    public const string Acrylic = "acrylic";

    /// <summary>Elevated card surface.</summary>
    public const string Elevated = "elevated";

    /// <summary>Recessed / sunk well surface.</summary>
    public const string Recessed = "recessed";
}

/// <summary>A glassmorphic, elevated, or recessed container surface.</summary>
public sealed record Surface(
    IReadOnlyList<Node> Children,
    [property: JsonPropertyName("surfaceKind")] string Kind = SurfaceKind.Acrylic,
    string? Title = null,
    string? Subtitle = null,
    double Padding = 18) : Node
{
    /// <inheritdoc />
    public bool Equals(Surface? other) => other is not null && Kind == other.Kind && Title == other.Title && Subtitle == other.Subtitle && Padding.Equals(other.Padding) && Children.SequenceEqual(other.Children);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Kind, Title, Children.Count);
}

/// <summary>A single tab item in a <see cref="Tabs"/> bar.</summary>
public sealed record TabItem(string Id, string Label, ButtonAction Action, string? Icon = null);

/// <summary>A horizontal tab bar.</summary>
public sealed record Tabs(IReadOnlyList<TabItem> Items, string SelectedId) : Node
{
    /// <inheritdoc />
    public bool Equals(Tabs? other) => other is not null && SelectedId == other.SelectedId && Items.SequenceEqual(other.Items);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(SelectedId, Items.Count);
}

/// <summary>A hairline section divider with an optional label.</summary>
public sealed record Divider(string? Label = null) : Node;

/// <summary>Fixed empty space between elements.</summary>
public sealed record Spacer(double Size = 12) : Node;

/// <summary>A list of entries.</summary>
public sealed record Collection(IReadOnlyList<Entry> Entries) : Node
{
    /// <inheritdoc />
    public bool Equals(Collection? other) => other is not null && Entries.SequenceEqual(other.Entries);

    /// <inheritdoc />
    public override int GetHashCode() => Entries.Count;
}

/// <summary>One entry of a collection.</summary>
public sealed record Entry(string Id, string Title, string? Subtitle)
{
    /// <summary>Text shown at the end of the entry, such as a size or a quota; null for none.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Trailing { get; init; }
}

/// <summary>Display heading rendered in <c>InterDisplay</c>.</summary>
public sealed record Heading(string Value, int Level = 2, string? Subtitle = null) : Node;

/// <summary>Read-only text.</summary>
public sealed record Text(string Value) : Node
{
    /// <summary>Semantic tone (<c>neutral</c>, <c>muted</c>, <c>strong</c>, <c>accent</c>, <c>done</c>, <c>working</c>, <c>error</c>).</summary>
    public string Tone { get; init; } = "neutral";

    /// <summary>True to enforce tabular figures (<c>tnum</c>).</summary>
    public bool Tabular { get; init; }

    /// <summary>True for monospace / URI rendering.</summary>
    public bool Mono { get; init; }

    /// <summary>Optional font size override in logical pixels.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Size { get; init; }
}

/// <summary>Compact status badge.</summary>
public sealed record Badge(string Label, string Tone = "neutral", string? Icon = null) : Node;

/// <summary>22px desktop tag pill with tabular figures and optional Lucide icon.</summary>
public sealed record TagPill(string Label, string Tone = "neutral", string? Icon = null) : Node;

/// <summary>An editable text field.</summary>
public sealed record TextField(string Name, string? Value, string? Placeholder) : Node
{
    /// <summary>Optional action invoked when the field is submitted.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ButtonAction? OnSubmit { get; init; }
}

/// <summary>A 36x20px pill toggle switch.</summary>
public sealed record Switch(string Label, bool IsOn, ButtonAction? Action = null, bool Locked = false) : Node;

/// <summary>An option in a <see cref="Select"/> control.</summary>
public sealed record SelectOption(string Id, string Label, ButtonAction? Action = null);

/// <summary>A desktop dropdown / segmented selector.</summary>
public sealed record Select(string Label, string Selected, IReadOnlyList<SelectOption> Options) : Node
{
    /// <inheritdoc />
    public bool Equals(Select? other) => other is not null && Label == other.Label && Selected == other.Selected && Options.SequenceEqual(other.Options);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Label, Selected, Options.Count);
}

/// <summary>A progress bar with label and percentage.</summary>
public sealed record Progress(string Label, double Value, string? Detail = null) : Node;

/// <summary>An inline status/alert banner.</summary>
public sealed record InfoBar(string Title, string Message, string Tone = "neutral", Button? Action = null) : Node;

/// <summary>A transient message.</summary>
public sealed record Toast(string Message) : Node;

/// <summary>A card: a title, a body and child nodes below them (the os <c>card</c> control).</summary>
/// <param name="Title">The card title.</param>
/// <param name="Body">The card body.</param>
/// <param name="Children">Nodes shown below the body, in order.</param>
public sealed record Card(string Title, string Body, IReadOnlyList<Node> Children) : Node
{
    /// <inheritdoc />
    public bool Equals(Card? other) => other is not null && Title == other.Title && Body == other.Body && Children.SequenceEqual(other.Children);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Title, Body, Children.Count);
}

/// <summary>The button tones the renderer knows (the os <c>ControlVisual.Tone</c>): only neutral and accent.</summary>
public static class ButtonTone
{
    /// <summary>The ordinary tone.</summary>
    public const string Neutral = "neutral";

    /// <summary>The single primary action of a surface.</summary>
    public const string Accent = "accent";
}

/// <summary>What a button click calls: an operation of a neuron, addressed by key in the viewer's brain, with string arguments.</summary>
/// <param name="Neuron">The neuron key the click is dispatched to.</param>
/// <param name="Operation">The operation called.</param>
/// <param name="Arguments">The call arguments, in order.</param>
public sealed record ButtonAction(string Neuron, string Operation, IReadOnlyList<string> Arguments)
{
    /// <inheritdoc />
    public bool Equals(ButtonAction? other) => other is not null && Neuron == other.Neuron && Operation == other.Operation && Arguments.SequenceEqual(other.Arguments);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Neuron, Operation, Arguments.Count);
}

/// <summary>A button (the os <c>button</c> control).</summary>
/// <param name="Label">The label.</param>
/// <param name="Action">What a click calls.</param>
/// <param name="Enabled">False shows the button disabled.</param>
/// <param name="Tone"><see cref="ButtonTone.Neutral"/> or <see cref="ButtonTone.Accent"/>.</param>
public sealed record Button(string Label, ButtonAction Action, bool Enabled = true, string Tone = ButtonTone.Neutral) : Node
{
    /// <summary>Visual style (<c>primary</c>, <c>secondary</c>, <c>ghost</c>, <c>danger</c>, <c>link</c>).</summary>
    public string Style { get; init; } = "secondary";

    /// <summary>Optional Lucide icon slug.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Icon { get; init; }

    /// <summary>True for the compact 30px desktop button height.</summary>
    public bool Small { get; init; }
}

/// <summary>Sortable tabular data card with optional <c>neuron://</c> source URI.</summary>
public sealed record DataTable(
    string Title,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    string? SourceNeuronUri = null,
    string From = "DigitalBrain") : Node
{
    /// <inheritdoc />
    public bool Equals(DataTable? other) =>
        other is not null &&
        Title == other.Title &&
        SourceNeuronUri == other.SourceNeuronUri &&
        From == other.From &&
        Columns.SequenceEqual(other.Columns) &&
        Rows.Count == other.Rows.Count &&
        Rows.Zip(other.Rows).All(pair => pair.First.SequenceEqual(pair.Second));

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Title, Columns.Count, Rows.Count);
}

/// <summary>A single data point in a <see cref="Chart"/>.</summary>
public sealed record ChartPointNode(string Label, double Value);

/// <summary>Single-series or period-comparison chart card (<c>columns</c> or <c>line</c>).</summary>
public sealed record Chart(
    [property: JsonPropertyName("chartKind")] string Kind,
    string Title,
    IReadOnlyList<ChartPointNode> Points,
    IReadOnlyList<double>? ComparePoints = null,
    string SeriesName = "",
    string CompareName = "",
    string? SourceNeuronUri = null,
    string From = "DigitalBrain",
    string Prefix = "") : Node
{
    /// <inheritdoc />
    public bool Equals(Chart? other) =>
        other is not null &&
        Kind == other.Kind &&
        Title == other.Title &&
        SeriesName == other.SeriesName &&
        CompareName == other.CompareName &&
        SourceNeuronUri == other.SourceNeuronUri &&
        From == other.From &&
        Prefix == other.Prefix &&
        Points.SequenceEqual(other.Points) &&
        ((ComparePoints is null && other.ComparePoints is null) ||
         (ComparePoints is not null && other.ComparePoints is not null && ComparePoints.SequenceEqual(other.ComparePoints)));

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Kind, Title, Points.Count);
}

/// <summary>One pre-filled change row inside an <see cref="ApprovalCardNode"/>.</summary>
public sealed record ApprovalRowNode(string Label, string OldValue, string NewValue, string? Amount = null, bool Warn = false);

/// <summary>Pre-filled inline approval card.</summary>
public sealed record ApprovalCardNode(
    string Id,
    string Title,
    string Summary,
    IReadOnlyList<ApprovalRowNode> Rows,
    string ValueLabel = "Value",
    string SystemName = "DigitalBrain",
    string Status = "pending",
    ButtonAction? OnApprove = null,
    ButtonAction? OnReject = null,
    string? SourceNeuronUri = null) : Node
{
    /// <inheritdoc />
    public bool Equals(ApprovalCardNode? other) =>
        other is not null &&
        Id == other.Id &&
        Title == other.Title &&
        Summary == other.Summary &&
        ValueLabel == other.ValueLabel &&
        SystemName == other.SystemName &&
        Status == other.Status &&
        SourceNeuronUri == other.SourceNeuronUri &&
        Equals(OnApprove, other.OnApprove) &&
        Equals(OnReject, other.OnReject) &&
        Rows.SequenceEqual(other.Rows);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Id, Title, Status, Rows.Count);
}

/// <summary>One numbered option inside a <see cref="ChoiceCardNode"/>.</summary>
public sealed record ChoiceOptionNode(string Title, string? Detail = null, ButtonAction? Action = null, string? Icon = null);

/// <summary>Disambiguation choice card (2 to 5 options).</summary>
public sealed record ChoiceCardNode(
    string Id,
    string Question,
    IReadOnlyList<ChoiceOptionNode> Options,
    int? PickedIndex = null) : Node
{
    /// <inheritdoc />
    public bool Equals(ChoiceCardNode? other) =>
        other is not null &&
        Id == other.Id &&
        Question == other.Question &&
        PickedIndex == other.PickedIndex &&
        Options.SequenceEqual(other.Options);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Id, Question, Options.Count);
}

/// <summary>Pre-filled outbound message draft card (email, Slack, SMS).</summary>
public sealed record DraftCardNode(
    string Id,
    string Channel,
    string To,
    string Body,
    string From,
    string? Subject = null,
    string Status = "pending",
    ButtonAction? OnSend = null,
    ButtonAction? OnDiscard = null) : Node;

/// <summary>A key-value field in a <see cref="RecordCardNode"/>.</summary>
public sealed record RecordFieldNode(string Label, string Value, bool Warn = false);

/// <summary>Structured record summary card.</summary>
public sealed record RecordCardNode(
    string ObjectLabel,
    string Name,
    IReadOnlyList<RecordFieldNode> Fields,
    string? Subtitle = null,
    string LinkLabel = "Open Neuron",
    ButtonAction? OnOpen = null) : Node
{
    /// <inheritdoc />
    public bool Equals(RecordCardNode? other) =>
        other is not null &&
        ObjectLabel == other.ObjectLabel &&
        Name == other.Name &&
        Subtitle == other.Subtitle &&
        LinkLabel == other.LinkLabel &&
        Equals(OnOpen, other.OnOpen) &&
        Fields.SequenceEqual(other.Fields);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(ObjectLabel, Name, Fields.Count);
}

/// <summary>Secure credential request card.</summary>
public sealed record SecretCardNode(
    string Name,
    string Why,
    string SavedIn = "Variables and secrets",
    string Status = "pending",
    ButtonAction? OnEnter = null,
    ButtonAction? OnSkip = null) : Node;

/// <summary>A step in a <see cref="JobCardNode"/>.</summary>
public sealed record JobStepNode(string Label, string Status = "waiting", string? Detail = null);

/// <summary>Multi-step background job progress card.</summary>
public sealed record JobCardNode(
    string Title,
    IReadOnlyList<JobStepNode> Steps,
    double Progress = 0,
    string Status = "running",
    string? ResultText = null,
    ButtonAction? OnCancel = null) : Node
{
    /// <inheritdoc />
    public bool Equals(JobCardNode? other) =>
        other is not null &&
        Title == other.Title &&
        Progress.Equals(other.Progress) &&
        Status == other.Status &&
        ResultText == other.ResultText &&
        Equals(OnCancel, other.OnCancel) &&
        Steps.SequenceEqual(other.Steps);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Title, Status, Steps.Count);
}

/// <summary>A 3D neuron node in a <see cref="BrainSceneNode"/>.</summary>
public sealed record BrainGraphNode(
    string Id,
    string Label,
    string Uri,
    string Kind,
    double X,
    double Y,
    double Z,
    string Status = "idle",
    string? Subtitle = null,
    ButtonAction? OnSelect = null);

/// <summary>A directed synapse edge in a <see cref="BrainSceneNode"/>.</summary>
public sealed record BrainGraphSynapse(
    string FromId,
    string ToId,
    string? SignalLabel = null,
    bool Active = false);

/// <summary>Interactive 3D neural topology scene.</summary>
public sealed record BrainSceneNode(
    IReadOnlyList<BrainGraphNode> Nodes,
    IReadOnlyList<BrainGraphSynapse> Synapses,
    string? SelectedId = null) : Node
{
    /// <inheritdoc />
    public bool Equals(BrainSceneNode? other) =>
        other is not null &&
        SelectedId == other.SelectedId &&
        Nodes.SequenceEqual(other.Nodes) &&
        Synapses.SequenceEqual(other.Synapses);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(SelectedId, Nodes.Count, Synapses.Count);
}

/// <summary>Reference to a live neuron control in the same brain.</summary>
public sealed record NeuronRef([property: JsonPropertyName("refKind")] string Kind, string NeuronUri) : Node;

/// <summary>Semantic tone for status badges and stat cards.</summary>
public enum BadgeTone
{
    /// <summary>Default neutral metadata tone.</summary>
    Neutral = 0,

    /// <summary>Primary brand/accent tone.</summary>
    Accent = 1,

    /// <summary>Completed / healthy green tone.</summary>
    Done = 2,

    /// <summary>In-progress / warning amber tone.</summary>
    Working = 3,

    /// <summary>Destructive / failure red tone.</summary>
    Error = 4,
}

/// <summary>Studio-grade desktop application shell with optional Acrylic sidebar, top toolbar, main body, and right-hand inspector.</summary>
public sealed record AppShellNode(
    string Key,
    Node? Sidebar,
    Node Body,
    Node? Toolbar = null,
    Node? Inspector = null) : Node;

/// <summary>Navigation rail / sidebar item with icon, active indicator, and optional count/status badge.</summary>
public sealed record NavItemNode(
    string Key,
    string Label,
    string Icon,
    string Action,
    bool Selected = false,
    string? Badge = null) : Node;

/// <summary>Compact KPI / telemetry card with tabular-numeral (<c>tnum</c>) metric value and optional delta pill.</summary>
public sealed record StatCardNode(
    string Key,
    string Label,
    string Value,
    string? Delta = null,
    BadgeTone Tone = BadgeTone.Neutral,
    string? Icon = null) : Node;

/// <summary>Ergonomic high-level builders for authoring <c>App&lt;TState&gt;</c> desktop views in ~30 lines of C#.</summary>
public static class Ui
{
    /// <summary>Creates a studio-grade desktop app shell layout with optional sidebar, toolbar, and right inspector pane.</summary>
    public static AppShellNode AppShell(
        string key,
        Node? sidebar,
        Node body,
        Node? toolbar = null,
        Node? inspector = null) =>
        new(key, sidebar, body, toolbar, inspector);

    /// <summary>Creates a sidebar navigation item with a Lucide icon key, operation name, selection state, and optional badge.</summary>
    public static NavItemNode NavItem(
        string key,
        string label,
        string icon,
        string action,
        bool selected = false,
        string? badge = null) =>
        new(key, label, icon, action, selected, badge);

    /// <summary>Creates a tabular-numeral (<c>tnum</c>) KPI stat card with optional delta pill and semantic tone.</summary>
    public static StatCardNode StatCard(
        string key,
        string label,
        string value,
        string? delta = null,
        BadgeTone tone = BadgeTone.Neutral,
        string? icon = null) =>
        new(key, label, value, delta, tone, icon);
}

/// <summary>Where an app publishes what it shows: its side and main views and transient toasts. The host renders the latest of each.</summary>
public interface IPresenter
{
    /// <summary>Replaces the side view, the main view, or both; a null view keeps the current one.</summary>
    void Present(View? side, View? main);

    /// <summary>Shows a transient message.</summary>
    void Toast(Toast toast);
}
