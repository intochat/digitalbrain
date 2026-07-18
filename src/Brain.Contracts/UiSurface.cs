namespace Brain.Contracts;

[GenerateSerializer, Alias("brain.ui-action.v1")]
public sealed record UiAction(
    [property: Id(0)] string Id,
    [property: Id(1)] string Label,
    [property: Id(2)] long ExpectedRevision);

[GenerateSerializer, Alias("brain.ui-block.v1")]
public sealed record UiBlock(
    [property: Id(0)] string Kind,
    [property: Id(1)] string Text,
    [property: Id(2)] IReadOnlyList<UiAction> Actions);

[GenerateSerializer, Alias("brain.ui-surface.v1")]
public sealed record UiSurface(
    [property: Id(0)] string SurfaceId,
    [property: Id(1)] long Revision,
    [property: Id(2)] IReadOnlyList<UiBlock> Blocks);

[GenerateSerializer, Alias("brain.ui-surface-snapshot.v1")]
public sealed record UiSurfaceSnapshot(
    [property: Id(0)] UiSurface Surface);

[GenerateSerializer, Alias("brain.ui-patch-operation.v1")]
public sealed record UiPatchOperation(
    [property: Id(0)] string Op,
    [property: Id(1)] string Path,
    [property: Id(2)] string Value);

[GenerateSerializer, Alias("brain.ui-surface-patch.v1")]
public sealed record UiSurfacePatch(
    [property: Id(0)] string SurfaceId,
    [property: Id(1)] long FromRevision,
    [property: Id(2)] long ToRevision,
    [property: Id(3)] IReadOnlyList<UiPatchOperation> Operations);
