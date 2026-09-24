namespace DigitalBrain.Apps;

// A workspace window a saved declarative app reopens: kind 'table' resolves through the brain,
// any other kind is a surface inside the owning workspace.
[GenerateSerializer, Alias("apps.window")]
public sealed record AppWindow
{
    [Id(0)] public required string WindowId { get; init; }
    [Id(1)] public required string Title { get; init; }
    [Id(2)] public required string Kind { get; init; }
    [Id(3)] public required string NeuronId { get; init; }
}
