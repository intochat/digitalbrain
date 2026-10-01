using System.Net.Http.Json;
using System.Text.Json.Nodes;
using DigitalBrain.Flutter;
using Microsoft.Playwright;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;

namespace IntoChat.Tests.E2E.Workspace;

public sealed class ShellPersistenceFacts(IntoChatHostFixture host) : BrainFact(host)
{
    [Fact(Timeout = 300_000)]
    public async Task FreshBrowserRestoresWorkspaceWithoutDeviceStorage()
    {
        var ct = TestContext.Current.CancellationToken;
        var brain = Brain;
        var page = await OpenPageAsync(ct);
        var id = await WorkspaceBrowser.CreateProjectAsync(page, "Cloud-only workspace");
        var durable = false;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var state = await brain.HttpClient.GetFromJsonAsync<JsonObject>("/shell/state", ct);
            durable = state?["snapshot"]?["projects"]?.AsArray().Any(project => project?["id"]?.GetValue<string>() == id) == true;
            if (durable) { break; }
            await Task.Delay(100, ct);
        }
        Assert.True(durable, "Workspace was not durably acknowledged.");
        await using var fresh = await brain.OpenBrowserAsync(ct);
        await fresh.Page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^Workspaces") }).ClickAsync();
        await Assertions.Expect(fresh.Page.GetByRole(AriaRole.Menuitemcheckbox, new() { Name = "Cloud-only workspace", Exact = true })).ToBeVisibleAsync();
        Assert.Equal(0, await fresh.Page.EvaluateAsync<int>("() => Object.keys(localStorage).filter(k => k.includes('workspace')).length"));
    }

    [Fact(Timeout = 240_000)]
    public async Task HttpClientsShareDurableStateAndRejectStaleWrites()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await IntoChatE2ETest.StartAsync(ct);
        var http = brain.HttpClient;
        var empty = await http.GetFromJsonAsync<JsonObject>("/shell/state", ct);
        Assert.Equal(0, empty!["revision"]!.GetValue<int>());
        Assert.Null(empty["snapshot"]);
        var snapshot = JsonNode.Parse("""{"version":1,"projects":[{"id":"restored","name":"Saved workspace","conversations":[{"id":"chat","draft":"unfinished","messages":[{"role":"user","content":"remember this"}]}],"artifacts":[]}],"selectedProjectId":"restored","settings":{"theme":"dark"}}""");
        using var first = await http.PutAsJsonAsync("/shell/state", new { expectedRevision = 0, operationId = Guid.NewGuid().ToString(), snapshot }, ct);
        first.EnsureSuccessStatusCode();
        using var fresh = new HttpClient { BaseAddress = http.BaseAddress };
        var restored = await fresh.GetFromJsonAsync<JsonObject>("/shell/state", ct);
        Assert.True(JsonNode.DeepEquals(snapshot, restored!["snapshot"]));
        var savedOperationId = Guid.NewGuid().ToString();
        using var saved = await fresh.PutAsJsonAsync("/shell/state", new { expectedRevision = 1, operationId = savedOperationId, snapshot }, ct);
        saved.EnsureSuccessStatusCode();
        using var stale = await http.PutAsJsonAsync("/shell/state", new { expectedRevision = 1, operationId = Guid.NewGuid().ToString(), snapshot }, ct);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var replay = await http.PutAsJsonAsync("/shell/state", new { expectedRevision = 1, operationId = savedOperationId, snapshot }, ct);
        replay.EnsureSuccessStatusCode();
        Assert.Equal(2, (await replay.Content.ReadFromJsonAsync<JsonObject>(ct))!["revision"]!.GetValue<int>());
    }
}
