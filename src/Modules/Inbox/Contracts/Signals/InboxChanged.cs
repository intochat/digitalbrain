using DigitalBrain.Contracts;

namespace DigitalBrain.Inbox.Signals;

[GenerateSerializer, Alias("inbox.changed")]
public sealed record InboxChanged(
    [property: Id(0)] string WorkspaceId,
    [property: Id(1)] string ItemId,
    [property: Id(2)] InboxItemKind Kind,
    [property: Id(3)] bool Resolved) : Signal;
