using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Inbox.Signals;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.Inbox;

[GrainType("inbox-feed")]
internal sealed class InboxNeuron([PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<InboxState> store)
    : Neuron<InboxState>(store), IInboxFeed
{
    private const int MaxItems = 200;

    public async Task<string> Post(InboxItemDraft item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var workspaceId = this.GetPrimaryKeyString();
        var now = DateTimeOffset.UtcNow;
        var next = Clone();
        var existing = next.Items.FirstOrDefault(candidate => candidate.GroupingKey == item.GroupingKey && !candidate.IsResolved);
        InboxItem posted;
        if (existing is null)
        {
            posted = new InboxItem
            {
                Id = Guid.NewGuid().ToString("N"),
                Kind = item.Kind,
                Title = item.Title,
                GroupingKey = item.GroupingKey,
                Detail = item.Detail,
                IntentId = item.IntentId,
                AppId = item.AppId,
                WindowId = item.WindowId,
                ReceiptId = item.ReceiptId,
                DeepLink = DeepLink(workspaceId, item),
                Actions = DefaultActions(item.Kind),
                Count = 1,
                CreatedAt = now,
                LastSeenAt = now,
            };
            next.Items.Insert(0, posted);
            if (next.Items.Count > MaxItems) { next.Items.RemoveRange(MaxItems, next.Items.Count - MaxItems); }
        }
        else
        {
            // Repeats collapse into the first item so the first error and its deep link survive.
            posted = existing with { Count = existing.Count + 1, LastSeenAt = now, IsRead = false, DigestSent = false };
            next.Items[next.Items.IndexOf(existing)] = posted;
        }

        await Save(next, new InboxChanged(workspaceId, posted.Id, posted.Kind, posted.IsResolved));
        return posted.Id;
    }

    [ReadOnly]
    public Task<InboxSnapshot> Read()
    {
        var items = Snapshot.Items.OrderByDescending(item => item.LastSeenAt).ToList();
        return Task.FromResult(new InboxSnapshot
        {
            Items = items,
            UnreadCount = items.Count(item => !item.IsRead && !item.IsResolved),
        });
    }

    public Task MarkRead(string itemId) => Update(itemId, item => item with { IsRead = true });

    public Task Resolve(string itemId) => Update(itemId, item => item with { IsRead = true, IsResolved = true });

    public async Task<int> DigestApprovals(TimeSpan minimumWait, string recipient)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipient);
        var cutoff = DateTimeOffset.UtcNow - minimumWait;
        var next = Clone();
        var waiting = next.Items
            .Where(item => item.Kind == InboxItemKind.ApprovalWaiting
                && !item.IsResolved && !item.DigestSent && item.LastSeenAt <= cutoff)
            .ToList();
        if (waiting.Count == 0) { return 0; }

        var sender = ServiceProvider.GetRequiredService<IEmailSender>();
        var body = string.Join(Environment.NewLine, waiting.Select(item => $"- {item.Title} ({item.LastSeenAt:u})"));
        await sender.SendAsync(recipient, $"{waiting.Count} approval(s) still waiting", body);
        foreach (var item in waiting) { next.Items[next.Items.IndexOf(item)] = item with { DigestSent = true }; }

        await Save(next, new InboxChanged(this.GetPrimaryKeyString(), waiting[0].Id, waiting[0].Kind, false));
        return waiting.Count;
    }

    private async Task Update(string itemId, Func<InboxItem, InboxItem> change)
    {
        var workspaceId = this.GetPrimaryKeyString();
        var next = Clone();
        var item = next.Items.FirstOrDefault(candidate => candidate.Id == itemId)
            ?? throw new KeyNotFoundException($"Inbox item '{itemId}' was not found.");
        var updated = change(item);
        next.Items[next.Items.IndexOf(item)] = updated;
        await Save(next, new InboxChanged(workspaceId, updated.Id, updated.Kind, updated.IsResolved));
    }

    private InboxState Clone() => new() { Items = [.. Snapshot.Items] };

    private static string? DeepLink(string workspaceId, InboxItemDraft item)
    {
        if (!string.IsNullOrWhiteSpace(item.WindowId)) { return $"/workspaces/{workspaceId}/windows/{item.WindowId}"; }
        if (!string.IsNullOrWhiteSpace(item.ReceiptId)) { return $"/receipts/{item.ReceiptId}"; }
        return null;
    }

    private static List<InboxAction> DefaultActions(InboxItemKind kind) => kind switch
    {
        InboxItemKind.ApprovalWaiting =>
        [
            new() { Id = "approve", Label = "Approve" },
            new() { Id = "reject", Label = "Reject" },
        ],
        InboxItemKind.AutomationFailed =>
        [
            new() { Id = "open", Label = "Open run" },
            new() { Id = "retry", Label = "Retry" },
        ],
        _ => [new() { Id = "open", Label = "Open" }],
    };
}
