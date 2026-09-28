using System.Net.Http.Json;
using System.Text.Json.Nodes;
using DigitalBrain.Flutter;
using Microsoft.Playwright;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;

namespace IntoChat.Tests.E2E.Workspace;

public sealed class ShellPersistenceFacts
{
    [Fact(Timeout = 240_000)]
    public async Task EstablishedWorkspaceImportsWithinClientDeadline()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await IntoChatE2ETest.StartAsync(ct);
        var snapshot = new JsonObject
        {
            ["version"] = 1,
            ["projects"] = new JsonArray(Enumerable.Range(0, 6).Select(project => (JsonNode?)new JsonObject
            {
                ["id"] = $"project-{project}",
                ["conversations"] = new JsonArray(new JsonObject
                {
                    ["id"] = "chat",
                    ["messages"] = new JsonArray(Enumerable.Range(0, 180).Select(message => (JsonNode?)new JsonObject
                    {
                        ["role"] = "user", ["content"] = $"{project}:{message}:" + new string('x', 900),
                        ["metadata"] = new JsonObject { ["usage"] = new JsonObject { ["tokens"] = message } }
                    }).ToArray())
                })
            }).ToArray())
        };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var operationId = Guid.NewGuid().ToString();
        using var imported = await brain.HttpClient.PostAsJsonAsync("/shell/import", new { expectedRevision = 0, operationId, snapshot }, deadline.Token);
        imported.EnsureSuccessStatusCode();
        var restored = await brain.HttpClient.GetFromJsonAsync<JsonObject>("/shell/state", deadline.Token);
        Assert.True(JsonNode.DeepEquals(snapshot, restored!["snapshot"]));
        using var replay = await brain.HttpClient.PostAsJsonAsync("/shell/import", new { expectedRevision = 0, operationId, snapshot }, deadline.Token);
        replay.EnsureSuccessStatusCode();
        Assert.Equal(1, (await replay.Content.ReadFromJsonAsync<JsonObject>(deadline.Token))!["revision"]!.GetValue<int>());
    }

    [Fact(Timeout = 300_000)]
    public async Task FreshBrowserRestoresWorkspaceWithoutDeviceStorage()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await IntoChatE2ETest.Create()
            .ConfigureModule<FlutterModule>(flutter => flutter.RunWebApp()).StartAsync(ct);
        var page = brain.Page;
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
    public Task HttpClientsShareDurableStateAndRejectStaleWrites() => VerifyDurableState(false, TestContext.Current.CancellationToken);

    [Fact(Timeout = 240_000, Skip = "Resource restart leaves stale Orleans silo membership; tracked in persistence implementation rollout notes.")]
    public Task ServerRestartRestoresDurableState() => VerifyDurableState(true, TestContext.Current.CancellationToken);

    private static async Task VerifyDurableState(bool restart, CancellationToken ct)
    {
        await using var brain = await IntoChatE2ETest.StartAsync(ct);
        var http = brain.HttpClient;
        var empty = await http.GetFromJsonAsync<JsonObject>("/shell/state", ct);
        Assert.Equal(0, empty!["revision"]!.GetValue<int>());
        Assert.Null(empty["snapshot"]);
        var snapshot = JsonNode.Parse("""{"version":1,"projects":[{"id":"restored","name":"Saved workspace","conversations":[{"id":"chat","draft":"unfinished","messages":[{"role":"user","content":"remember this"}]}],"artifacts":[]}],"selectedProjectId":"restored","settings":{"theme":"dark"}}""");
        var operationId = Guid.NewGuid().ToString();
        using var imported = await http.PostAsJsonAsync("/shell/import", new { expectedRevision = 0, operationId, snapshot }, ct);
        imported.EnsureSuccessStatusCode();
        using var fresh = new HttpClient { BaseAddress = http.BaseAddress };
        var restored = await fresh.GetFromJsonAsync<JsonObject>("/shell/state", ct);
        Assert.True(JsonNode.DeepEquals(snapshot, restored!["snapshot"]));
        using var saved = await fresh.PutAsJsonAsync("/shell/state", new { expectedRevision = 1, operationId = Guid.NewGuid().ToString(), snapshot }, ct);
        saved.EnsureSuccessStatusCode();
        using var stale = await http.PutAsJsonAsync("/shell/state", new { expectedRevision = 1, operationId = Guid.NewGuid().ToString(), snapshot }, ct);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var replay = await http.PostAsJsonAsync("/shell/import", new { expectedRevision = 0, operationId, snapshot }, ct);
        replay.EnsureSuccessStatusCode();
        Assert.Equal(2, (await replay.Content.ReadFromJsonAsync<JsonObject>(ct))!["revision"]!.GetValue<int>());
        if (!restart) { return; }
        var commands = brain.Application.Services.GetRequiredService<ResourceCommandService>();
        var restarted = await commands.ExecuteCommandAsync("IntoChat", KnownResourceCommands.RestartCommand, ct);
        Assert.True(restarted.Success, restarted.Message);
        await brain.Application.Services.GetRequiredService<ResourceNotificationService>().WaitForResourceHealthyAsync("IntoChat", ct);
        var afterRestart = await fresh.GetFromJsonAsync<JsonObject>("/shell/state", ct);
        Assert.Equal(2, afterRestart!["revision"]!.GetValue<int>());
        Assert.True(JsonNode.DeepEquals(snapshot, afterRestart["snapshot"]));
    }
}
