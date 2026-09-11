using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.UI;

[GenerateSerializer]
[Alias("ui.workspace-record")]
public sealed record WorkspaceRecord(
    [property: Id(0)] string Name,
    [property: Id(1)] string CorrelationId,
    [property: Id(2)] string Title,
    [property: Id(3)] DateTimeOffset UpdatedAt);
