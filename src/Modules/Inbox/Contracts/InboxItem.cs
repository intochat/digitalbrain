namespace DigitalBrain.Inbox;

[GenerateSerializer, Alias("inbox.action")]
public sealed record InboxAction
{
    [Id(0)] public required string Id { get; init; }
    [Id(1)] public required string Label { get; init; }
    [Id(2)] public string? Route { get; init; }
}

[GenerateSerializer, Alias("inbox.item")]
public sealed record InboxItem
{
    [Id(0)] public required string Id { get; init; }
    [Id(1)] public required InboxItemKind Kind { get; init; }
    [Id(2)] public required string Title { get; init; }
    [Id(3)] public required string GroupingKey { get; init; }
    [Id(4)] public string? Detail { get; init; }
    [Id(5)] public string? IntentId { get; init; }
    [Id(6)] public string? AppId { get; init; }
    [Id(7)] public string? WindowId { get; init; }
    [Id(8)] public string? ReceiptId { get; init; }
    [Id(9)] public string? DeepLink { get; init; }
    [Id(10)] public List<InboxAction> Actions { get; init; } = [];
    [Id(11)] public bool IsRead { get; init; }
    [Id(12)] public bool IsResolved { get; init; }
    [Id(13)] public int Count { get; init; }
    [Id(14)] public DateTimeOffset CreatedAt { get; init; }
    [Id(15)] public DateTimeOffset LastSeenAt { get; init; }
    [Id(16)] public bool DigestSent { get; init; }
}

[GenerateSerializer, Alias("inbox.snapshot")]
public sealed record InboxSnapshot
{
    [Id(0)] public required List<InboxItem> Items { get; init; }
    [Id(1)] public int UnreadCount { get; init; }
}
