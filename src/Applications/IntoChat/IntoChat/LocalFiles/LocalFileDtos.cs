using DigitalBrain.Flutter.Collection;
using Orleans;
namespace IntoChat.LocalFiles;

public sealed record DirectoryPage(string FolderId, string Label, string? ParentId, IReadOnlyList<CollectionItem> Items, int? NextOffset, IReadOnlyList<DirectoryCrumb>? Breadcrumbs = null);
public sealed record DirectoryCrumb(string Id, string Label);
[GenerateSerializer, Alias("intochat.image-asset")]
public sealed record ImageAsset([property: Id(0)] string Id, [property: Id(1)] string Name, [property: Id(2)] int Width, [property: Id(3)] int Height, [property: Id(4)] string Fingerprint, [property: Id(5)] string SourceEntryId, [property: Id(6)] string DocumentId);
[GenerateSerializer, Alias("intochat.saved-file")]
public sealed record SavedFile([property: Id(0)] string EntryId, [property: Id(1)] string Name, [property: Id(2)] string Checksum, [property: Id(3)] long Bytes);
internal sealed record FileHandle(string Scope, string Root, string RelativePath, long? Length = null, long? ModifiedTicks = null, long? CreatedTicks = null);