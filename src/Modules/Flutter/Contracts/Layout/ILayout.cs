using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.Flutter.Layout;

[Alias("layout"), Orleans.Metadata.DefaultGrainType(UIVocabulary.LayoutType)]
public interface ILayout : INeuron
{
    Task Set(LayoutDefinition definition, long expectedRevision);
    [ReadOnly] Task<LayoutState> Read();
}

[GenerateSerializer, Alias("ui.layout-definition")]
public sealed record LayoutDefinition([property: Id(0)] string Mode, [property: Id(1)] IReadOnlyList<UiChildRef> Children, [property: Id(2)] double Gap = 12, [property: Id(3)] IReadOnlyList<double>? Extents = null);

[GenerateSerializer, Alias("ui.layout-state")]
public sealed class LayoutState
{
    [Id(0)] public string Name { get; set; } = "";
    [Id(1)] public long Revision { get; set; }
    [Id(2)] public LayoutDefinition Definition { get; set; } = new("column", []);
}

[GenerateSerializer, Alias("ui.layout-changed")]
public sealed record LayoutChanged([property: Id(0)] string Name, [property: Id(1)] long Revision) : Signal;