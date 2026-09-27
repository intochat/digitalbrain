using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Apps;
using DigitalBrain.Microsoft.CSharp;

namespace IntoChat.Tests.E2E.Packages;

// Two signed-in people share a C# app through the product routes: Alice publishes, Bob installs it
// in one request, customizes it, forks and improves it, and Alice accepts his change back.
public sealed class PackageSharingFacts
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact(Timeout = 900_000)]
    public async Task PeopleShareInstallForkAndContributeCSharpAppsOverHttp()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await IntoChatE2ETest.Create()
            .WithResourceEnvironment(new Dictionary<string, string> { ["IntoChat__CSharp__AllowActivation"] = "true" })
            .StartAsync(ct);
        using var alice = await People.SignedIn(brain.HttpClient, "alice", ct);
        using var bob = await People.SignedIn(brain.HttpClient, "bob", ct);

        var original = await People.Send(alice.Client, HttpMethod.Post, "/packages/alice/researcher/revisions",
            new { content = ResearcherPackage.Content("Research"), message = "Research briefs" }, ct);
        await People.Send(alice.Client, HttpMethod.Post, "/packages/alice/researcher/publish", new { }, ct);
        var listings = await People.Send(bob.Client, HttpMethod.Get, "/packages", null, ct);
        Assert.Contains(listings.EnumerateArray(), listing => listing.GetProperty("revision").GetString() == original.GetProperty("id").GetString());

        using (var forged = await bob.Client.PostAsJsonAsync("/packages/alice/researcher/revisions",
            new { content = ResearcherPackage.Content("Forged"), message = "Not mine", expectedHead = original.GetProperty("id").GetString() }, Json, ct))
        { Assert.Equal(HttpStatusCode.Forbidden, forged.StatusCode); }

        var app = $"/workspaces/{bob.Workspace}/packages/alice/researcher";
        await People.Send(bob.Client, HttpMethod.Post, app, new { }, ct);
        Assert.Equal("Research (plain): What is Orleans?", await Ask(bob.Client, app, "What is Orleans?", ct));
        using (var foreign = await alice.Client.GetAsync(app, ct)) { Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode); }

        await People.Send(bob.Client, HttpMethod.Post, app + "/configure", new { settings = new { style = "bullets" } }, ct);
        Assert.Equal("Research (bullets): What is Orleans?", await Ask(bob.Client, app, "What is Orleans?", ct));

        var fork = await People.Send(bob.Client, HttpMethod.Post, "/packages/alice/researcher/fork", new { }, ct);
        Assert.Equal("bob", fork.GetProperty("id").GetProperty("owner").GetString());
        var summaries = await People.Send(bob.Client, HttpMethod.Post, "/packages/bob/researcher/revisions",
            new { content = ResearcherPackage.Content("Summary"), message = "Summarize instead", expectedHead = fork.GetProperty("head").GetString() }, ct);

        var proposal = await People.Send(bob.Client, HttpMethod.Post, "/packages/alice/researcher/proposals",
            new { source = new { owner = "bob", name = "researcher" }, title = "Summaries" }, ct);
        await People.Send(alice.Client, HttpMethod.Post, $"/packages/alice/researcher/proposals/{proposal.GetProperty("number").GetInt32()}/accept", null, ct);
        var published = await People.Send(alice.Client, HttpMethod.Post, "/packages/alice/researcher/publish", new { }, ct);
        Assert.Equal(summaries.GetProperty("id").GetString(), published.GetProperty("published").GetString());

        await People.Send(bob.Client, HttpMethod.Post, app + "/upgrade", new { }, ct);
        Assert.Equal("Summary (bullets): What is Orleans?", await Ask(bob.Client, app, "What is Orleans?", ct));

        var uninstalled = await People.Send(bob.Client, HttpMethod.Delete, app, null, ct);
        Assert.Equal((int)AppStatus.Uninstalled, uninstalled.GetProperty("app").GetProperty("status").GetInt32());
    }

    // The first answer waits for the container to build the script; an exited script fails fast.
    private static async Task<string?> Ask(HttpClient client, string app, string question, CancellationToken ct)
    {
        var invocation = await People.Send(client, HttpMethod.Post, app + "/invocations", new { operation = "research", input = question }, ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        while (invocation.GetProperty("status").GetInt32() == (int)InvocationStatus.Pending)
        {
            var view = await People.Send(client, HttpMethod.Get, app, null, timeout.Token);
            Assert.NotEqual((int)CSharpFileStatus.Exited, view.GetProperty("file").GetProperty("status").GetInt32());
            await Task.Delay(500, timeout.Token);
            invocation = await People.Send(client, HttpMethod.Get, $"{app}/invocations/{invocation.GetProperty("id").GetGuid()}", null, timeout.Token);
        }
        Assert.Equal((int)InvocationStatus.Completed, invocation.GetProperty("status").GetInt32());
        return invocation.GetProperty("output").GetString();
    }
}
