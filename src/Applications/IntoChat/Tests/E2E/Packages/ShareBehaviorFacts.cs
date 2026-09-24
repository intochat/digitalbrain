using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Behavior;

namespace IntoChat.Tests.E2E.Packages;

// Alice builds an automation in her behavior console and shares it with one request; the package
// carries its code and its deployed settings, and Bob runs it with his own value.
public sealed class ShareBehaviorFacts
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact(Timeout = 900_000)]
    public async Task AnAutomationIsSharedAsAPackageThatOthersInstallWithTheirOwnSettings()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await IntoChatE2ETest.Create()
            .WithResourceEnvironment(new Dictionary<string, string> { ["IntoChat__BehaviorAuthoring__AllowActivation"] = "true" })
            .StartAsync(ct);
        using var alice = await People.SignedIn(brain.HttpClient, "alice", ct);
        using var bob = await People.SignedIn(brain.HttpClient, "bob", ct);
        var automation = $"/workspaces/{alice.Workspace}/behaviors/greeter";

        using (var nothingToShare = await alice.Client.PostAsJsonAsync(automation + "/share", new { }, Json, ct))
        { Assert.True(nothingToShare.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Conflict, $"Sharing nothing returned {nothingToShare.StatusCode}."); }

        await People.Send(alice.Client, HttpMethod.Post, automation + "/draft",
            new { expectedRevision = 0, operationId = Guid.NewGuid(), source = GreeterSource, tests = GreeterTests, moduleIds = Array.Empty<string>() }, ct);
        await People.Send(alice.Client, HttpMethod.Post, automation + "/description",
            new { expectedRevision = 0, name = "Greeter", purpose = "Greets whoever runs it.", triggers = Array.Empty<string>(), effects = Array.Empty<string>() }, ct);
        var check = await People.Send(alice.Client, HttpMethod.Post, automation + "/checks", new { revision = 1, operationId = Guid.NewGuid() }, ct);
        while (check.GetProperty("status").ToString() is "0" or "1" or "2" or "Queued" or "Building" or "Testing")
        { check = await People.Send(alice.Client, HttpMethod.Get, $"{automation}/checks/{check.GetProperty("operationId").GetGuid()}", null, ct); }
        Assert.True(check.TryGetProperty("artifact", out var artifact) && artifact.ValueKind == JsonValueKind.Object, check.ToString());
        await People.Send(alice.Client, HttpMethod.Post, automation + "/deploy",
            new { expectedRevision = 0, operationId = Guid.NewGuid(), artifact, configurationJson = """{"Behavior__Greeting":"hello"}""" }, ct);

        var shared = await People.Send(alice.Client, HttpMethod.Post, automation + "/share", new { }, ct);
        var reshared = await People.Send(alice.Client, HttpMethod.Post, automation + "/share", new { }, ct);

        Assert.Equal("alice", shared.GetProperty("id").GetProperty("owner").GetString());
        Assert.Equal("greeter", shared.GetProperty("id").GetProperty("name").GetString());
        var published = shared.GetProperty("published").GetString();
        Assert.Equal(published, shared.GetProperty("head").GetString());
        Assert.Equal(published, reshared.GetProperty("published").GetString());
        Assert.Single(reshared.GetProperty("history").EnumerateArray());
        var revision = await People.Send(bob.Client, HttpMethod.Get, $"/packages/alice/greeter/revisions/{published}", null, ct);
        var manifest = revision.GetProperty("content").GetProperty("manifest");
        Assert.Equal("Greeter", manifest.GetProperty("title").GetString());
        var setting = Assert.Single(manifest.GetProperty("settings").EnumerateArray());
        Assert.Equal(("Greeting", "hello"), (setting.GetProperty("name").GetString(), setting.GetProperty("defaultValue").GetString()));

        var installed = await People.Send(bob.Client, HttpMethod.Post, $"/workspaces/{bob.Workspace}/packages/alice/greeter",
            new { settings = new { Greeting = "hi" } }, ct);
        var program = brain.Get<IBehaviorProgram>(installed.GetProperty("app").GetProperty("behaviorProgram").GetString()!);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(120));
        while (!(await program.ReadLogs(0, 200, timeout.Token)).Entries.Any(entry => entry.Message.Contains("GREETING:hi", StringComparison.Ordinal)))
        {
            Assert.NotEqual(BehaviorExecutionState.Failed, (await program.Read(timeout.Token)).State);
            await Task.Delay(250, timeout.Token);
        }
    }

    private const string GreeterSource = """
        using DigitalBrain.Core;
        using Microsoft.Extensions.Configuration;
        await BehaviorApp.RunAsync<Greeter>(args, _ => []);
        public sealed class Greeter(IConfiguration configuration) : IBehavior
        {
            public static string Line(string? greeting) => "GREETING:" + (greeting ?? "none");
            public async Task RunAsync(CancellationToken cancellation = default)
            {
                Console.WriteLine(Line(configuration["Behavior:Greeting"]));
                await Task.Delay(Timeout.Infinite, cancellation);
            }
        }
        """;

    private const string GreeterTests = """
        public sealed class GreeterFacts
        {
            [Xunit.Fact]
            public void GreetsWithTheConfiguredGreeting() => Xunit.Assert.Equal("GREETING:hi", Greeter.Line("hi"));
        }
        """;
}
