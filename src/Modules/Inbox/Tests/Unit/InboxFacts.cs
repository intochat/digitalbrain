using DigitalBrain.Inbox;
using DigitalBrain.Inbox.Signals;
using DigitalBrain.Testing;
using DigitalBrain.Testing.Unit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Modules.Inbox.Tests.Unit;

public sealed class InboxFacts
{
    [Fact]
    public async Task PostedItemIsReadableWithItsSourceAndDeepLink()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<InboxModule>().StartAsync(ct);
        var inbox = brain.Get<IInboxFeed>("ws-1");

        var id = await inbox.Post(new InboxItemDraft
        {
            Kind = InboxItemKind.AutomationFailed,
            Title = "Daily lead run failed",
            GroupingKey = "automation:lead-run",
            Detail = "HttpRequestException: connection refused",
            IntentId = "intent-1",
            AppId = "leadgenerator",
            WindowId = "window-leads",
        });

        var snapshot = await inbox.Read();
        var item = Assert.Single(snapshot.Items);
        Assert.Equal(id, item.Id);
        Assert.Equal(InboxItemKind.AutomationFailed, item.Kind);
        Assert.Equal("HttpRequestException: connection refused", item.Detail);
        Assert.Equal("intent-1", item.IntentId);
        Assert.Equal("leadgenerator", item.AppId);
        Assert.Equal("/workspaces/ws-1/windows/window-leads", item.DeepLink);
        Assert.Equal(1, item.Count);
        Assert.False(item.IsRead);
        Assert.Equal(1, snapshot.UnreadCount);
    }

    [Fact]
    public async Task RepeatsGroupOnGroupingKeyAndKeepTheFirstError()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<InboxModule>().StartAsync(ct);
        var inbox = brain.Get<IInboxFeed>("ws-2");

        var first = await inbox.Post(new InboxItemDraft
        {
            Kind = InboxItemKind.AutomationFailed,
            Title = "Run failed",
            GroupingKey = "automation:run",
            Detail = "first error",
        });
        var second = await inbox.Post(new InboxItemDraft
        {
            Kind = InboxItemKind.AutomationFailed,
            Title = "Run failed",
            GroupingKey = "automation:run",
            Detail = "second error",
        });

        Assert.Equal(first, second);
        var item = Assert.Single((await inbox.Read()).Items);
        Assert.Equal(2, item.Count);
        Assert.Equal("first error", item.Detail);
    }

    [Fact]
    public async Task ResolveMarksTheItemResolvedAndPublishesAChange()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<InboxModule>().StartAsync(ct);
        var inbox = brain.Get<IInboxFeed>("ws-3");
        await using var changes = await brain.Observe<InboxChanged>(inbox, ct);

        var id = await inbox.Post(new InboxItemDraft
        {
            Kind = InboxItemKind.ApprovalWaiting,
            Title = "Approve refund",
            GroupingKey = "approval:refund",
        });
        await changes.NextAsync(ct: ct);
        await inbox.Resolve(id);

        var change = await changes.NextAsync(ct: ct);
        Assert.Equal(id, change.ItemId);
        Assert.True(change.Resolved);
        var item = Assert.Single((await inbox.Read()).Items);
        Assert.True(item.IsResolved);
        Assert.True(item.IsRead);
    }

    [Fact]
    public async Task DigestSendsOnlyApprovalsWaitingPastTheThreshold()
    {
        var ct = TestContext.Current.CancellationToken;
        var sender = new InMemoryEmailSender();
        await using var brain = await UnitTest.Create().WithModule<InboxModule>()
            .ConfigureSilo(silo => silo.Services.AddSingleton<IEmailSender>(sender))
            .StartAsync(ct);
        var inbox = brain.Get<IInboxFeed>("ws-4");
        await inbox.Post(new InboxItemDraft
        {
            Kind = InboxItemKind.ApprovalWaiting,
            Title = "Approve refund",
            GroupingKey = "approval:refund",
        });

        Assert.Equal(0, await inbox.DigestApprovals(TimeSpan.FromHours(1), "owner@example.com"));
        Assert.Empty(sender.Sent);

        Assert.Equal(1, await inbox.DigestApprovals(TimeSpan.Zero, "owner@example.com"));
        var email = Assert.Single(sender.Sent);
        Assert.Equal("owner@example.com", email.Recipient);
        Assert.Contains("Approve refund", email.Body, StringComparison.Ordinal);

        Assert.Equal(0, await inbox.DigestApprovals(TimeSpan.Zero, "owner@example.com"));
        Assert.Single(sender.Sent);
    }
}
