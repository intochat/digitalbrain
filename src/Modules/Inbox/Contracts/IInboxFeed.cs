using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.Inbox;

public enum InboxItemKind
{
    AutomationResult = 0,
    AutomationFailed = 1,
    ApprovalWaiting = 2,
    ConnectionExpired = 3,
    ComputeLimitAlert = 4,
    AppDisabled = 5,
}

[GenerateSerializer, Alias("inbox.item-draft")]
public sealed record InboxItemDraft
{
    [Id(0)] public required InboxItemKind Kind { get; init; }
    [Id(1)] public required string Title { get; init; }
    [Id(2)] public required string GroupingKey { get; init; }
    [Id(3)] public string? Detail { get; init; }
    [Id(4)] public string? IntentId { get; init; }
    [Id(5)] public string? AppId { get; init; }
    [Id(6)] public string? WindowId { get; init; }
    [Id(7)] public string? ReceiptId { get; init; }
}

// Keyed by workspace id; named IInboxFeed so it does not collide with the legacy UI-kit IInbox it replaces.
[Alias("inbox-feed")]
[Orleans.Metadata.DefaultGrainType("inbox-feed")]
public interface IInboxFeed : INeuron
{
    Task<string> Post(InboxItemDraft item);

    [ReadOnly, Alias("read")]
    Task<InboxSnapshot> Read();

    [Alias("mark-read")]
    Task MarkRead(string itemId);

    [Alias("resolve")]
    Task Resolve(string itemId);

    [Alias("digest-approvals")]
    Task<int> DigestApprovals(TimeSpan minimumWait, string recipient);
}
