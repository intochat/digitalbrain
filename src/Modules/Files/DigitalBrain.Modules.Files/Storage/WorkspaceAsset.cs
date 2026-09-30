using Orleans;

namespace DigitalBrain.Files;

[GenerateSerializer, Alias("intochat.workspace-asset")]
public sealed record WorkspaceAsset([property: Id(0)] ImageAsset Image, [property: Id(1)] long Bytes, [property: Id(2)] DateTimeOffset CreatedAt);
