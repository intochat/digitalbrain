using System.Net.Http.Json;
using System.Text.Json;
using System.Net;
using DigitalBrain.Apps;
using DigitalBrain.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Modules.Apps.Tests.Http;

public sealed class AppDocumentHttpFacts
{
    [Fact(Timeout = 120_000)]
    public async Task DraftDocumentsStayRevisionBoundAndBelongToTheirAuthorOverHttp()
    {
        var ct = TestContext.Current.CancellationToken;
        // Authoring routes mount only when the composition's sandbox can run; a fake runnable
        // sandbox stands in for the container, since nothing here executes a script.
        await using var brain = await ModuleTest.Create().WithModule<AppsModule>()
            .ConfigureSilo(silo => silo.Services.AddSingleton<IScriptSandbox>(new RunnableSandbox()))
            .WithHttpEdge()
            .StartAsync(ct);
        const string author = "blocks-author";
        const string reader = "blocks-reader";
        using var alice = await People.SignedIn(brain.HttpClient, author, ct);
        using var bob = await People.SignedIn(brain.HttpClient, reader, ct);
        var name = "blocks-" + Guid.NewGuid().ToString("N");
        var document = new AppAuthoringDocument(1, "", [new(Guid.NewGuid().ToString("N"), "Open", "Draw UI", ["behaviors/open.cs"], [])], []);
        var files = new Dictionary<string, string> { ["app.spec.md"] = "\n\n### Open\n\nDraw UI\n\n", ["app.authoring.json"] = JsonSerializer.Serialize(document, new JsonSerializerOptions(JsonSerializerDefaults.Web)), ["behaviors/open.cs"] = "// old source" };
        var content = new PackageContent(new("Blocks", "Small behaviors", [new("open", "Open")], [], Runtime: "prompt"), "", files);
        var first = await People.Send(alice.Client, HttpMethod.Post, $"/packages/{author}/{name}/revisions", new { content, message = "Original" }, ct);
        var revision = first.GetProperty("id").GetString()!;
        var reference = new PackageRevisionRef(PackageId.Create(author, name), revision);
        var path = "/packages/drafts/" + Guid.NewGuid().ToString("N");
        var imported = await People.Send(alice.Client, HttpMethod.Post, path + "/import", new { revision = reference, expectedRevision = 0 }, ct);
        Assert.Equal(document.Behaviors[0].Id, imported.GetProperty("draft").GetProperty("document").GetProperty("behaviors")[0].GetProperty("id").GetString());
        var version = imported.GetProperty("draft").GetProperty("revision").GetInt64();
        await People.Send(alice.Client, HttpMethod.Put, path + "/document", new { expectedRevision = version, document }, ct);
        using var conflict = await alice.Client.PutAsJsonAsync(path + "/document", new { expectedRevision = version, document }, ct);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        using var denied = await bob.Client.PostAsJsonAsync(path + "/import", new { revision = reference, expectedRevision = 0 }, ct);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var shown = await People.Send(alice.Client, HttpMethod.Get, $"/packages/{author}/{name}/spec?revision={revision}", null, ct);
        Assert.Equal("// old source", shown.GetProperty("files").GetProperty("behaviors/open.cs").GetString());
        Assert.True(shown.GetProperty("canEdit").GetBoolean());
        var other = await People.Send(bob.Client, HttpMethod.Get, $"/packages/{author}/{name}/spec?revision={revision}", null, ct);
        Assert.False(other.GetProperty("canEdit").GetBoolean());
    }

    private sealed class RunnableSandbox : IScriptSandbox
    {
        public bool CanRun => true;
        public string AuthoringDescription => "Test sandbox";
        public Task<ScriptContractCatalog> ReadContracts(IReadOnlyList<string> modules, CancellationToken cancellationToken)
            => Task.FromResult(new ScriptContractCatalog([], [], ""));
        public ScriptCompilationCheck Check(IReadOnlyDictionary<string, string> files) => new(true, []);
    }
}
