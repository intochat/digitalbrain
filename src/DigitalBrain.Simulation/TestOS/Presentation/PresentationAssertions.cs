using System.Reflection;
using System.Text.Json;
using DigitalBrain.Invocation;
using DigitalBrain.UI;
using DigitalBrain.Units;

namespace DigitalBrain.Testing;

/// <summary>A presentation does not hold what a test looked for.</summary>
/// <param name="message">What was missing and what is there.</param>
public sealed class PresentationAssertionException(string message) : Exception(message);

/// <summary>
/// One presented view read as a test reads it, without a renderer: its entries, texts and fields, found by id.
/// The accessors return plain values, so any assertion library works on them.
/// </summary>
public sealed class PresentedView
{
    /// <summary>Creates a presented view snapshot.</summary>
    public PresentedView(string part, View? view)
    {
        Part = part;
        View = view;
        Nodes = view is null ? [] : [.. Walk(view.Root)];
    }

    /// <summary>Creates a PresentedView from a PublishedView by deserializing its View wire JSON.</summary>
    public static PresentedView From(PublishedView published)
    {
        ArgumentNullException.ThrowIfNull(published);
        var view = JsonSerializer.Deserialize<View>(published.View.GetRawText(), RuntimeProtocol.OptionsFor(typeof(View)));
        return new PresentedView(published.Alias, view);
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
    public IReadOnlyList<Field> Fields => [.. Nodes.OfType<Field>()];

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
