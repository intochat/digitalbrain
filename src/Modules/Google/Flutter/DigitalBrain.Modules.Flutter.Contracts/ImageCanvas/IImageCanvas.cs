using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.Flutter.ImageCanvas;

[Alias("imagecanvas"), Orleans.Metadata.DefaultGrainType(UIVocabulary.ImageCanvasType)]
public interface IImageCanvas : INeuron
{
    Task Set(ImageCanvasDefinition definition, long expectedRevision);
    Task RequestEdit(ImageRecipe recipe, long expectedDocumentRevision, string operationId);
    [ReadOnly] Task<ImageCanvasState> Read();
}

[GenerateSerializer, Alias("ui.imagecanvas-definition")]
public sealed record ImageCanvasDefinition([property: Id(0)] string AssetId, [property: Id(1)] int Width, [property: Id(2)] int Height, [property: Id(3)] ImageRecipe Recipe, [property: Id(4)] long DocumentRevision);

[GenerateSerializer, Alias("ui.imagecanvas-state")]
public sealed class ImageCanvasState
{
    [Id(0)] public string Name { get; set; } = "";
    [Id(1)] public long Revision { get; set; }
    [Id(2)] public ImageCanvasDefinition Definition { get; set; } = new("", 0, 0, new(), 0);
}

[GenerateSerializer, Alias("ui.imagecanvas-changed")]
public sealed record ImageCanvasChanged([property: Id(0)] string Name, [property: Id(1)] long Revision) : Signal;

[GenerateSerializer, Alias("ui.imagecanvas-edit-requested")]
public sealed record ImageCanvasEditRequested([property: Id(0)] string Name, [property: Id(1)] ImageRecipe Recipe, [property: Id(2)] long DocumentRevision, [property: Id(3)] string OperationId) : Signal;