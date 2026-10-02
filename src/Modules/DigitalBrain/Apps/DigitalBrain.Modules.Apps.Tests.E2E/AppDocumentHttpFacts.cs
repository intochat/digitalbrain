using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Apps;

namespace DigitalBrain.Modules.Apps.Tests.E2E;

public sealed class AppDocumentHttpFacts(ReferenceBrainFixture host) : BrainFact(host)
{
    [Fact(Timeout = 900_000)]
    public async Task DraftDocumentsStayRevisionBoundAndBelongToTheirAuthorOverHttp()
    {
        var ct = TestContext.Current.CancellationToken;
        using var alice = await People.SignedIn(Brain.HttpClient, "alice", ct);
        using var bob = await People.SignedIn(Brain.HttpClient, "bob", ct);
        var name = "blocks-" + Guid.NewGuid().ToString("N");
        var document = new AppAuthoringDocument(1, "", [new(Guid.NewGuid().ToString("N"), "Open", "Draw UI", ["behaviors/open.cs"], [])], []);
        var files = new Dictionary<string, string> { ["app.spec.md"] = "\n\n### Open\n\nDraw UI\n\n", ["app.authoring.json"] = JsonSerializer.Serialize(document, new JsonSerializerOptions(JsonSerializerDefaults.Web)), ["behaviors/open.cs"] = "// old source" };
        var content = new PackageContent(new("Blocks", "Small behaviors", [new("open", "Open")], [], Runtime: "prompt"), "", files);
        var first = await People.Send(alice.Client, HttpMethod.Post, $"/packages/alice/{name}/revisions", new { content, message = "Original" }, ct);
        var revision = first.GetProperty("id").GetString()!;
        var reference = new PackageRevisionRef(PackageId.Create("alice", name), revision);
        var path = "/packages/drafts/" + Guid.NewGuid().ToString("N");
        var imported = await People.Send(alice.Client, HttpMethod.Post, path + "/import", new { revision = reference, expectedRevision = 0 }, ct);
        Assert.Equal(document.Behaviors[0].Id, imported.GetProperty("draft").GetProperty("document").GetProperty("behaviors")[0].GetProperty("id").GetString());
        var version = imported.GetProperty("draft").GetProperty("revision").GetInt64();
        await People.Send(alice.Client, HttpMethod.Put, path + "/document", new { expectedRevision = version, document }, ct);
        using var conflict = await alice.Client.PutAsJsonAsync(path + "/document", new { expectedRevision = version, document }, ct);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        using var denied = await bob.Client.PostAsJsonAsync(path + "/import", new { revision = reference, expectedRevision = 0 }, ct);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var shown = await People.Send(alice.Client, HttpMethod.Get, $"/packages/alice/{name}/spec?revision={revision}", null, ct);
        Assert.Equal("// old source", shown.GetProperty("files").GetProperty("behaviors/open.cs").GetString());
        Assert.True(shown.GetProperty("canEdit").GetBoolean());
        var other = await People.Send(bob.Client, HttpMethod.Get, $"/packages/alice/{name}/spec?revision={revision}", null, ct);
        Assert.False(other.GetProperty("canEdit").GetBoolean());
    }
}
