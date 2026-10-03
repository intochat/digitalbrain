using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Xunit;
namespace DigitalBrain.Modules.Flutter.Tests.E2E;

[Collection(FlutterBackendCollection.Name)]
public sealed class ShellPersistenceHttpFacts(FlutterBackendFixture host)
{
    [Fact(Timeout = 240_000)]
    public async Task HttpClientsShareDurableStateAndRejectStaleWrites()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await host.LeaseAsync(ct);
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
