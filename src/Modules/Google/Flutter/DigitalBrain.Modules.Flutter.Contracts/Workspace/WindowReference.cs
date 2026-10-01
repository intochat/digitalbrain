namespace DigitalBrain.Flutter.Workspace;

// One typed window reference: a window shows exactly one neuron, named by kind and id. Tables,
// surfaces and forms all use the same shape, so no window carries a bare string route. A caller
// resolves [NeuronId] through the brain to reach the neuron the window shows.
[GenerateSerializer, Alias("ui.window-reference")]
public sealed record WindowReference(
    [property: Id(0)] string Kind,
    [property: Id(1)] string NeuronId)
{
    public const string TableKind = "table";

    public static WindowReference Table(string tableId) => new(TableKind, tableId);

    public static WindowReference For(UiChildRef child) => new(child.Kind, child.Name);
}
