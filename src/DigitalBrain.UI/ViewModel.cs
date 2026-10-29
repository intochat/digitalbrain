using System.Text.Json.Serialization;

namespace DigitalBrain.UI;

/// <summary>A screen an app returns: a title and a tree of nodes. Renderer-neutral.</summary>
public sealed record View(string Title, Node Root);

/// <summary>A node of a view.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Column), "column")]
[JsonDerivedType(typeof(Collection), "collection")]
[JsonDerivedType(typeof(Text), "text")]
[JsonDerivedType(typeof(TextField), "textField")]
[JsonDerivedType(typeof(Toast), "toast")]
[JsonDerivedType(typeof(Card), "card")]
[JsonDerivedType(typeof(Button), "button")]
public abstract record Node;

/// <summary>Children laid out top to bottom.</summary>
public sealed record Column(IReadOnlyList<Node> Children) : Node
{
    /// <inheritdoc />
    public bool Equals(Column? other) => other is not null && Children.SequenceEqual(other.Children);

    /// <inheritdoc />
    public override int GetHashCode() => Children.Count;
}

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

/// <summary>Read-only text.</summary>
public sealed record Text(string Value) : Node;

/// <summary>An editable text field.</summary>
public sealed record TextField(string Name, string? Value, string? Placeholder) : Node;

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
public sealed record Button(string Label, ButtonAction Action, bool Enabled = true, string Tone = ButtonTone.Neutral) : Node;

/// <summary>Where an app publishes what it shows: its side and main views and transient toasts. The host renders the latest of each.</summary>
public interface IPresenter
{
    /// <summary>Replaces the side view, the main view, or both; a null view keeps the current one.</summary>
    void Present(View? side, View? main);

    /// <summary>Shows a transient message.</summary>
    void Toast(Toast toast);
}
