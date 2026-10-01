namespace DigitalBrain.Apps;

[GenerateSerializer, Alias("intochat.app-draft-entry")]
public sealed record AppDraftEntry(
    [property: Id(0)] string Id,
    [property: Id(1)] string Title,
    [property: Id(2)] AppDraftStatus Status,
    [property: Id(3)] DateTimeOffset UpdatedAt);

