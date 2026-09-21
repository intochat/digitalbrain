using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.Flutter.Surface;

[Alias("surface"), Orleans.Metadata.DefaultGrainType(UIVocabulary.SurfaceType)]
public interface ISurface : INeuron
{
    Task Set(SurfaceDefinition definition, long expectedRevision);
    [ReadOnly] Task<SurfaceState> Read();
}

[GenerateSerializer, Alias("ui.surface-definition")]
public sealed record SurfaceDefinition([property: Id(0)] string Title, [property: Id(1)] IReadOnlyList<UiChildRef> Children);

[GenerateSerializer, Alias("ui.surface-state")]
public sealed class SurfaceState
{
    [Id(0)] public string Name { get; set; } = "";
    [Id(1)] public long Revision { get; set; }
    [Id(2)] public SurfaceDefinition Definition { get; set; } = new("", []);
}

[GenerateSerializer, Alias("ui.surface-changed")]
public sealed record SurfaceChanged([property: Id(0)] string Name, [property: Id(1)] long Revision) : Signal;