using System.Reflection;
using DigitalBrain.UI;

namespace DigitalBrain.Testing;

/// <summary>What an installed app shows: the latest side and main <see cref="DigitalBrain.UI"/> views and every toast so far.</summary>
/// <param name="Side">The latest side view, or null before the app presents one.</param>
/// <param name="Main">The latest main view, or null before the app presents one.</param>
/// <param name="Toasts">Every toast, oldest first.</param>
public sealed record AppPresentation(View? Side, View? Main, IReadOnlyList<Toast> Toasts);

/// <summary>A presentation does not hold what a test looked for.</summary>
/// <param name="message">What was missing and what is there.</param>
public sealed class PresentationAssertionException(string message) : Exception(message);

/// <summary>
/// One presented view read as a test reads it, without a renderer: its entries, texts and fields, found by id.
/// The accessors return plain values, so any assertion library works on them.
/// </summary>
public sealed class PresentedView
{
    internal PresentedView(string part, View? view)
    {
        Part = part;
        View = view;
        Nodes = view is null ? [] : [.. Walk(view.Root)];
    }

    /// <summary>The part of the app this view is: <c>side</c> or <c>main</c>.</summary>
    public string Part { get; }

    /// <summary>The view, or null when the app has not presented this part.</summary>
    public View? View { get; }

    /// <summary>True when the app has presented this part.</summary>
    public bool IsSet => View is not null;

    /// <summary>The view title, or null when not set.</summary>
    public string? Title => View?.Title;

    /// <summary>Every node of the view, depth first.</summary>
    public IReadOnlyList<Node> Nodes { get; }

    /// <summary>The entries of every collection in the view, in order.</summary>
    public IReadOnlyList<Entry> Entries => [.. Nodes.OfType<Collection>().SelectMany(collection => collection.Entries)];

    /// <summary>The text values of the view, in order.</summary>
    public IReadOnlyList<string> Texts =>
    [
        .. Nodes.SelectMany(node => node switch
        {
            Text text => [text.Value],
            Heading { Subtitle: { Length: > 0 } sub } heading => new[] { heading.Value, sub },
            Heading heading => [heading.Value],
            InfoBar info => new[] { info.Title, info.Message },
            ApprovalCardNode approval => new[] { approval.Title, approval.Summary, approval.Status, approval.SourceNeuronUri ?? string.Empty }.Where(s => s.Length > 0).ToArray(),
            DataTable table => new[] { table.Title, table.SourceNeuronUri ?? string.Empty }.Where(s => s.Length > 0).ToArray(),
            Chart chart => new[] { chart.Title, chart.SourceNeuronUri ?? string.Empty }.Where(s => s.Length > 0).ToArray(),
            RecordCardNode record => new[] { record.ObjectLabel, record.Name, record.Subtitle ?? string.Empty }.Where(s => s.Length > 0).ToArray(),
            BrainSceneNode scene => new[] { $"3D BrainScene ({scene.Nodes.Count} nodes, {scene.Synapses.Count} synapses)" }.Concat(scene.Nodes.Select(n => $"{n.Label} ({n.Uri})")).ToArray(),
            NeuronRef nref => [nref.NeuronUri],
            _ => Array.Empty<string>(),
        }),
    ];

    /// <summary>The text fields of the view, in order.</summary>
    public IReadOnlyList<TextField> Fields => [.. Nodes.OfType<TextField>()];

    /// <summary>The ids <see cref="Find(string)"/> knows: entry ids and field names.</summary>
    public IReadOnlyList<string> Ids => [.. Entries.Select(entry => entry.Id).Concat(Fields.Select(textField => textField.Name))];

    /// <summary>The entry with id <paramref name="id"/>, else the text field named <paramref name="id"/>.</summary>
    /// <exception cref="PresentationAssertionException">Nothing has that id; the message lists the ids there are.</exception>
    public object Find(string id)
        => (object?)Entries.FirstOrDefault(entry => entry.Id == id)
            ?? Fields.FirstOrDefault(textField => textField.Name == id)
            ?? throw new PresentationAssertionException(
                $"The {Part} view has no entry or field '{id}'. Ids: {(Ids.Count == 0 ? "(none)" : string.Join(", ", Ids))}{(IsSet ? string.Empty : "; the app has not presented its " + Part + " view")}.");

    /// <summary>The entry with id <paramref name="id"/>.</summary>
    /// <exception cref="PresentationAssertionException">No entry has that id.</exception>
    public Entry Entry(string id)
        => Find(id) as Entry ?? throw new PresentationAssertionException($"'{id}' in the {Part} view is a field, not an entry.");

    /// <inheritdoc />
    public override string ToString() => View is null ? $"{Part}: (not set)" : $"{Part}: {View.Title} [{string.Join(", ", Ids)}]";

    private static IEnumerable<Node> Walk(Node node)
    {
        yield return node;
        var children = node switch
        {
            Column column => column.Children,
            Row row => row.Children,
            Grid grid => grid.Children,
            SplitPane split => [split.Leading, split.Trailing],
            Surface surface => surface.Children,
            Card card => card.Children,
            _ => [],
        };
        foreach (var child in children.SelectMany(Walk)) { yield return child; }
    }
}

// The presentation half of InstalledApp: an IPresenter handed to the app's constructor, plus the app's own Side and Main properties when it has them.
public sealed partial class InstalledApp<TApp>
{
    private readonly Lock _presentationGate = new();
    private readonly List<Toast> _toasts = [];
    private View? _side;
    private View? _main;
    private Exception? _presentationError;

    /// <summary>The latest presentation the app published.</summary>
    public AppPresentation Presentation
    {
        get
        {
            lock (_presentationGate) { return new AppPresentation(_side, _main, [.. _toasts]); }
        }
    }

    /// <summary>The latest side view.</summary>
    public PresentedView Side => new("side", Presentation.Side);

    /// <summary>The latest main view.</summary>
    public PresentedView Main => new("main", Presentation.Main);

    /// <summary>Every toast message so far, oldest first.</summary>
    public IReadOnlyList<string> Toasts => [.. Presentation.Toasts.Select(toast => toast.Message)];

    /// <summary>The error the app's <c>Side</c> or <c>Main</c> property threw when last read, or null.</summary>
    public Exception? PresentationError
    {
        get
        {
            lock (_presentationGate) { return _presentationError; }
        }
    }

    /// <summary>The entry or field <paramref name="id"/> in the main view, else in the side view.</summary>
    /// <exception cref="PresentationAssertionException">Neither view has it; the message lists the ids there are.</exception>
    public object Find(string id)
    {
        var main = Main;
        if (main.Ids.Contains(id)) { return main.Find(id); }
        var side = Side;
        return side.Ids.Contains(id)
            ? side.Find(id)
            : throw new PresentationAssertionException(
                $"App '{Manifest.Id}' presents no entry or field '{id}'. Main ids: {List(main.Ids)}. Side ids: {List(side.Ids)}.");
    }

    private object? Service(Type type) => type == typeof(IPresenter) ? new Presenter(this) : _os.Services.GetService(type);

    // Reads the app's Side/RenderSide and Main/Render views, if it declares them, after each turn.
    private void Refresh()
    {
        var side = Read("Side") ?? InvokeRender("RenderSide");
        var main = Read("Main") ?? InvokeRender("Render");
        if (side is null && main is null) { return; }
        lock (_presentationGate)
        {
            if (side is { } s) { _side = s.View ?? _side; }
            if (main is { } m) { _main = m.View ?? _main; }
        }
    }

    private (View? View, bool)? InvokeRender(string methodName)
    {
        var method = typeof(TApp).GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public, Type.EmptyTypes);
        if (method is null || _app is null || !(typeof(View).IsAssignableFrom(method.ReturnType) || typeof(Node).IsAssignableFrom(method.ReturnType)))
        {
            return null;
        }
        try
        {
            var value = method.Invoke(_app, null);
            return (value as View ?? (value is Node node ? new View(methodName == "RenderSide" ? "Side" : "Main", node) : null), true);
        }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        {
            lock (_presentationGate) { _presentationError = error.InnerException; }
            return null;
        }
    }

    private (View? View, bool)? Read(string name)
    {
        var property = typeof(TApp).GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
        if (property is null || _app is null || !(typeof(View).IsAssignableFrom(property.PropertyType) || typeof(Node).IsAssignableFrom(property.PropertyType)))
        {
            return null;
        }
        try
        {
            var value = property.GetValue(_app);
            return (value as View ?? (value is Node node ? new View(name, node) : null), true);
        }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        {
            lock (_presentationGate) { _presentationError = error.InnerException; }
            return null;
        }
    }

    private void Present(View? side, View? main)
    {
        lock (_presentationGate)
        {
            _side = side ?? _side;
            _main = main ?? _main;
        }
    }

    private void Add(Toast toast)
    {
        ArgumentNullException.ThrowIfNull(toast);
        lock (_presentationGate) { _toasts.Add(toast); }
    }

    private static string List(IReadOnlyList<string> ids) => ids.Count == 0 ? "(none)" : string.Join(", ", ids);

    private sealed class Presenter(InstalledApp<TApp> app) : IPresenter
    {
        public void Present(View? side, View? main) => app.Present(side, main);

        public void Toast(Toast toast) => app.Add(toast);
    }
}
