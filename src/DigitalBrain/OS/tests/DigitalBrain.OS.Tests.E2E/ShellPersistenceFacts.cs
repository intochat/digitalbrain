using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Flutter;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace DigitalBrain.OS.Tests.E2E;

public sealed class ShellPersistenceFacts(ReferenceBrainFixture host) : BrainFact(host)
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

}
