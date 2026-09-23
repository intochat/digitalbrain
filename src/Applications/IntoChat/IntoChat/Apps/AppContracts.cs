using DigitalBrain.Contracts;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.ImageCanvas;
using IntoChat.LocalFiles;
using Orleans;
using Orleans.Concurrency;
namespace IntoChat.Apps;

[Alias("intochat.file-explorer"), Orleans.Metadata.DefaultGrainType("intochat.file-explorer")]
public interface IFileExplorer : INeuron
{
    Task<FileExplorerState> Navigate(string? folderId, int offset = 0, string sort = "name", string filter = "");
    Task<ImageDocumentState> OpenImage(string entryId);
    [ReadOnly] Task<FileExplorerState> Read();
}
[Alias("intochat.image-document"), Orleans.Metadata.DefaultGrainType("intochat.image-document")]
public interface IImageDocument : INeuron
{
    Task<ImageDocumentState> Open(ImageAsset asset);
    Task<ImageDocumentState> Apply(ImageEditCommand command, long expectedRevision, string operationId);
    Task<SaveTicket> PrepareSave(long expectedRevision, string operationId);
    Task<ImageDocumentState> CompleteSave(string operationId, SavedFile result);
    // A background-removed copy is appended while the original stays at revision zero.
    Task<ImageDocumentState> AddVersion(string kind, string assetId);
    [ReadOnly] Task<ImageDocumentState> Read();
}
[GenerateSerializer, Alias("intochat.image-edit-command")]
public sealed record ImageEditCommand([property: Id(0)] string Kind, [property: Id(1)] PenStroke? Stroke = null, [property: Id(2)] CropRect? Crop = null, [property: Id(3)] ImageRecipe? Recipe = null);
[GenerateSerializer, Alias("intochat.files-state")]
public sealed record FileExplorerState
{
    [Id(0)] public string? FolderId { get; init; }
    [Id(1)] public long Revision { get; init; }
    [Id(2)] public UiChildRef? Surface { get; init; }
}
[GenerateSerializer, Alias("intochat.image-document-state")]
public sealed record ImageDocumentState
{
    [Id(0)] public string Id { get; init; } = "";
    [Id(1)] public ImageAsset? Asset { get; init; }
    [Id(2)] public ImageRecipe Recipe { get; init; } = new();
    [Id(3)] public long Revision { get; init; }
    [Id(4)] public long LastSavedRevision { get; init; } = -1;
    [Id(5)] public UiChildRef? Surface { get; init; }
    [Id(6)] public Dictionary<string, string> Receipts { get; init; } = [];
    [Id(7)] public Dictionary<string, SaveTicket> Saves { get; init; } = [];
    [Id(8)] public IReadOnlyList<ImageVersion> Versions { get; init; } = [];
}
[GenerateSerializer, Alias("intochat.image-version")]
public sealed record ImageVersion([property: Id(0)] string VersionId, [property: Id(1)] string AssetId, [property: Id(2)] long Revision, [property: Id(3)] string Kind);
[GenerateSerializer, Alias("intochat.image-save-ticket")]
public sealed record SaveTicket([property: Id(0)] string OperationId, [property: Id(1)] long Revision, [property: Id(2)] ImageRecipe Recipe, [property: Id(3)] ImageAsset Asset, [property: Id(4)] SavedFile? Result = null);

internal static class ImageEdits
{
    public static ImageRecipe Apply(ImageRecipe current, ImageEditCommand command, int width, int height)
    {
        var next = command.Kind switch
        {
            "stroke" when command.Stroke is not null => current with { Strokes = [.. current.Strokes, command.Stroke] },
            "crop" when command.Crop is not null => current with { Crop = command.Crop },
            "reset" => new ImageRecipe(),
            "replace" when command.Recipe is not null => command.Recipe,
            _ => throw new ArgumentException("Unknown image edit command.")
        };
        next.Validate(width, height);
        return next;
    }
}