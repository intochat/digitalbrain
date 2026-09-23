using System.Net.Http.Json;
using DigitalBrain.Inbox;
using Xunit;

namespace IntoChat.Tests.E2E.Inbox;

public sealed class StructuredInboxFacts
{
    // Automations land in parallel, so the failed run is posted straight through IInboxFeed.
    [Fact]
    public async Task FailedAutomationItemAppearsWithItsFirstErrorAndResolves()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await IntoChatE2ETest.StartAsync(ct);
        var inbox = brain.Get<IInboxFeed>("ws-e2e");
        var id = await inbox.Post(new InboxItemDraft
        {
            Kind = InboxItemKind.AutomationFailed,
            Title = "Daily lead run failed",
            GroupingKey = "automation:daily-leads",
            Detail = "HttpRequestException: connection refused",
            IntentId = "intent-1",
            AppId = "leadgenerator",
            WindowId = "window-leads",
        });

        var snapshot = await brain.HttpClient.GetFromJsonAsync<InboxSnapshot>("/inbox/ws-e2e", ct);
        var item = Assert.Single(snapshot!.Items);
        Assert.Equal(id, item.Id);
        Assert.Equal(InboxItemKind.AutomationFailed, item.Kind);
        Assert.Equal("HttpRequestException: connection refused", item.Detail);
        Assert.Equal("/workspaces/ws-e2e/windows/window-leads", item.DeepLink);
        Assert.Equal(1, snapshot.UnreadCount);

        using var resolved = await brain.HttpClient.PostAsync($"/inbox/ws-e2e/items/{id}/resolve", null, ct);
        resolved.EnsureSuccessStatusCode();
        var afterResolve = await brain.HttpClient.GetFromJsonAsync<InboxSnapshot>("/inbox/ws-e2e", ct);
        Assert.True(Assert.Single(afterResolve!.Items).IsResolved);
        Assert.Equal(0, afterResolve.UnreadCount);
    }

    [Fact]
    public async Task EventsStreamPushesThePostedItem()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await IntoChatE2ETest.StartAsync(ct);
        var inbox = brain.Get<IInboxFeed>("ws-sse");

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await brain.HttpClient.GetAsync("/inbox/ws-sse/events", HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(deadline.Token);
        using var reader = new StreamReader(body);

        var id = await inbox.Post(new InboxItemDraft
        {
            Kind = InboxItemKind.ApprovalWaiting,
            Title = "Approve refund",
            GroupingKey = "approval:refund",
        });

        var pushed = false;
        while (await reader.ReadLineAsync(deadline.Token) is { } line)
        {
            if (line.Contains(id, StringComparison.Ordinal)) { pushed = true; break; }
        }

        Assert.True(pushed, "The posted item must be pushed over the events stream.");
    }
}
