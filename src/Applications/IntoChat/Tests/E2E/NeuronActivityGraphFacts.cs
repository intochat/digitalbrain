using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Contracts;
using DigitalBrain.Flutter.Inbox;

namespace IntoChat.Tests.E2E;

public sealed class NeuronActivityGraphFacts
{
    [Fact(Timeout = 180_000)]
    public async Task ScopedNeuronCallAndSignalReachProductActivityEndpoint()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await IntoChatE2ETest.StartAsync(ct);
        var scope = IntoChat.Workspace.WorkspaceScope.Create("owner", "activity-e2e");
        using (IntentContext.Begin("activity-run", scope.Id))
        {
            await brain.Get<IInbox>("activity-e2e-inbox").Appear("hello");
        }

        var snapshot = await brain.HttpClient.GetFromJsonAsync<JsonElement>(
            "/workspaces/activity-e2e/activity", ct);
        var events = snapshot.GetProperty("events").EnumerateArray().ToArray();
        Assert.Contains(events, item => item.GetProperty("type").GetString() == "Appear");
        Assert.Contains(events, item => item.GetProperty("type").GetString() == "InboxAppeared");
        Assert.Contains(events, item => item.GetProperty("type").GetString() == "InboxAppeared"
            && item.GetProperty("correlationId").GetString() == "activity-run");
        Assert.DoesNotContain("hello", snapshot.ToString());

        var other = await brain.HttpClient.GetFromJsonAsync<JsonElement>(
            "/workspaces/other/activity", ct);
        Assert.Empty(other.GetProperty("events").EnumerateArray());
    }
}
