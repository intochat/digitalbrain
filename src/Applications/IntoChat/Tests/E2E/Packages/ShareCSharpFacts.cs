using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Microsoft.CSharp;
using DigitalBrain.Sdk.Connectors;

namespace IntoChat.Tests.E2E.Packages;

// Alice writes a C# app and shares it with one request; the package carries its code and account
// slots, never Alice's settings, and Bob's install runs it with his own account.
public sealed class ShareCSharpFacts
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact(Timeout = 900_000)]
    public async Task ACSharpAppIsSharedWithoutSettingsAndInstalledWithTheRecipientsAccount()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await IntoChatE2ETest.Create()
            .WithResourceEnvironment(new Dictionary<string, string> { ["IntoChat__CSharp__AllowActivation"] = "true" })
            .StartAsync(ct);
        using var alice = await People.SignedIn(brain.HttpClient, "alice", ct);
        using var bob = await People.SignedIn(brain.HttpClient, "bob", ct);
        var greeter = $"/workspaces/{alice.Workspace}/csharp/greeter";

        using (var nothingToShare = await alice.Client.PostAsJsonAsync(greeter + "/share", new { }, Json, ct))
        { Assert.Equal(HttpStatusCode.NotFound, nothingToShare.StatusCode); }

        var written = await People.Send(alice.Client, HttpMethod.Put, greeter,
            new { source = GreeterSource, name = "Greeter", purpose = "Greets whoever runs it." }, ct);
        Assert.Equal("Greeter", written.GetProperty("description").GetProperty("name").GetString());

        var share = new { accounts = new[] { new { name = "twitter", source = "twitter", description = "Account to watch" } } };
        var shared = await People.Send(alice.Client, HttpMethod.Post, greeter + "/share", share, ct);
        var reshared = await People.Send(alice.Client, HttpMethod.Post, greeter + "/share", share, ct);

        Assert.Equal("alice", shared.GetProperty("id").GetProperty("owner").GetString());
        Assert.Equal("greeter", shared.GetProperty("id").GetProperty("name").GetString());
        var published = shared.GetProperty("published").GetString();
        Assert.Equal(published, shared.GetProperty("head").GetString());
        Assert.Equal(published, reshared.GetProperty("published").GetString());
        Assert.Single(reshared.GetProperty("history").EnumerateArray());
        var revision = await People.Send(bob.Client, HttpMethod.Get, $"/packages/alice/greeter/revisions/{published}", null, ct);
        var manifest = revision.GetProperty("content").GetProperty("manifest");
        Assert.Equal("Greeter", manifest.GetProperty("title").GetString());
        Assert.Empty(manifest.GetProperty("settings").EnumerateArray());
        Assert.Equal("twitter", Assert.Single(manifest.GetProperty("accounts").EnumerateArray()).GetProperty("name").GetString());

        using (var missing = await bob.Client.PostAsJsonAsync($"/workspaces/{bob.Workspace}/packages/alice/greeter", new { }, Json, ct))
        { Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode); }
        await brain.Get<IConnectors>("bob").Connect(new ConnectRequest
        {
            Source = "twitter", ConnectionId = "bob-twitter", Value = "test-token"
        }, new CallerContext
        {
            PrincipalId = "bob", AccountId = "bob", WorkspaceId = "bob",
            Kind = CallerKind.User, StampedBy = TrustedEdge.AuthenticatedHttp
        }, ct);

        var options = await People.Send(bob.Client, HttpMethod.Get,
            $"/workspaces/{bob.Workspace}/packages/alice/greeter/accounts", null, ct);
        Assert.Equal("bob-twitter", Assert.Single(options.GetProperty("slots").EnumerateArray())
            .GetProperty("accounts").EnumerateArray().Single().GetProperty("id").GetString());
        using (var wrong = await bob.Client.PostAsJsonAsync($"/workspaces/{bob.Workspace}/packages/alice/greeter",
            new { accounts = new { twitter = "alice-twitter" } }, Json, ct))
        { Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode); }

        var installed = await People.Send(bob.Client, HttpMethod.Post, $"/workspaces/{bob.Workspace}/packages/alice/greeter",
            new { accounts = new { twitter = "bob-twitter" } }, ct);
        Assert.Equal("bob-twitter", installed.GetProperty("app").GetProperty("accounts").GetProperty("twitter").GetString());
        var file = brain.Get<ICSharpFile>(installed.GetProperty("app").GetProperty("csharpFile").GetString()!);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        string logs;
        while (!(logs = await file.ReadLogs(200, timeout.Token)).Contains("GREETING:none ACCOUNT:bob-twitter", StringComparison.Ordinal))
        {
            Assert.True((await file.Read(timeout.Token)).Status != CSharpFileStatus.Exited, logs);
            await Task.Delay(500, timeout.Token);
        }
        await People.Send(bob.Client, HttpMethod.Delete, $"/workspaces/{bob.Workspace}/packages/alice/greeter", null, ct);
    }

    private const string GreeterSource = """
        await using var brain = await DigitalBrainClient.ConnectAsync(args);
        Console.WriteLine($"GREETING:{brain.Setting("Greeting") ?? "none"} ACCOUNT:{brain.Setting("Account__twitter")}");
        await Task.Delay(Timeout.Infinite, brain.Stopping);
        """;
}
