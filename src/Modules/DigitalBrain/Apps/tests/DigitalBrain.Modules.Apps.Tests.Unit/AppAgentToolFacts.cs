using System.Text.Json;
using DigitalBrain.AI.Agents;
using DigitalBrain.Apps;
using DigitalBrain.Kernel.Enforcement;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Modules.Apps.Tests.Unit;

public sealed class AppAgentToolFacts
{
    [Fact]
    public async Task BoundSelectionRejectsUpgradeAndUninstallBeforeInvoking()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<AppsModule>().StartAsync(ct);
        Caller.As("alice");
        await brain.AuthorizeCallerAsync();
        var scope = BrainScope.CurrentId();
        var id = PackageId.Create("alice", "selected");
        var package = brain.Get<IPackage>(id.ToString());
        var content = new PackageContent(new("Selected", "An app", [new("ask", "Ask")], [], Runtime: "prompt"), "");
        var first = await package.Commit(new(Guid.NewGuid(), null, content, "First"));
        var next = await package.Commit(new(Guid.NewGuid(), first.Id, content with { Manifest = content.Manifest with { Description = "Changed" } }, "Next"));
        var app = brain.Get<IApp>(scope + "/packages/" + id);
        await app.Install(new(Guid.NewGuid(), new(id, first.Id), new Dictionary<string, string>()));
        var source = Assert.Single(brain.SiloServices.GetServices<IAgentToolSource>());
        var names = new[] { AppToolName.For(id, "ask") };
        await using var selected = await source.OpenBoundAsync(id + "@" + first.Id, names, () => new(scope, "run", "call"), ct);
        await app.Upgrade(new(Guid.NewGuid(), new(id, next.Id)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Assert.Single(selected.Tools).InvokeAsync(new AIFunctionArguments { ["input"] = "hello" }, ct).AsTask());
        // Receiving-side revision check also covers a change between tool validation and Invoke.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => app.Invoke(new(Guid.NewGuid(), "ask", "hello", new(id, first.Id))));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.OpenBoundAsync(id + "@" + first.Id, names, () => new(scope, "run", "call"), ct));
        await using var refreshed = await source.OpenBoundAsync(id + "@" + next.Id, names, () => new(scope, "run", "next"), ct);
        await app.Uninstall(new(Guid.NewGuid()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Assert.Single(refreshed.Tools).InvokeAsync(new AIFunctionArguments { ["input"] = "hello" }, ct).AsTask());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.OpenBoundAsync(id + "@" + next.Id, names, () => new(scope, "run", "call"), ct));
    }

    [Fact]
    public async Task PendingAppToolReturnsAnErrorWithoutExternalCancellation()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<AppsModule>().StartAsync(ct);
        Caller.As("alice");
        await brain.AuthorizeCallerAsync();
        var scope = BrainScope.CurrentId();
        var id = PackageId.Create("alice", "pending");
        var package = brain.Get<IPackage>(id.ToString());
        var revision = await package.Commit(new(Guid.NewGuid(), null,
            new(new("Pending", "No responder", [new("ask", "Ask")], []), "// behavior"), "Initial"));
        await package.Publish(new(Guid.NewGuid(), revision.Id));
        var app = brain.Get<IApp>(scope + "/packages/" + id);
        await Task.WhenAll(app.Install(new(Guid.NewGuid(), new(id, revision.Id), new Dictionary<string, string>())),
            brain.Get<IApps>(scope).List()).WaitAsync(TimeSpan.FromSeconds(10), ct);
        var source = Assert.Single(brain.SiloServices.GetServices<IAgentToolSource>());
        await using var session = await source.OpenAsync([AppToolName.For(id, "ask")], () => new(scope, "run", "call"), ct);
        var result = Assert.IsType<JsonElement>(await Assert.Single(session.Tools)
            .InvokeAsync(new AIFunctionArguments { ["input"] = "hello" }, ct).AsTask().WaitAsync(TimeSpan.FromSeconds(35), ct));
        Assert.Equal("App invocation timed out.", result.GetProperty("error").GetString());
        Assert.True(result.GetProperty("isError").GetBoolean());
        Assert.Equal("app_timeout", result.GetProperty("code").GetString());
        Assert.Single(await app.Pending());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AppToolsInvokeTheirOwnAppAndReplayTheSameCallWithoutInvokingAgain(bool fail)
    {
        var ct = TestContext.Current.CancellationToken;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        await using var brain = await UnitTest.Create().WithModule<AppsModule>().StartAsync(ct);
        Caller.As("alice");
        await brain.AuthorizeCallerAsync();
        var scope = BrainScope.CurrentId();
        var id = PackageId.Create("alice", "echo");
        var other = PackageId.Create("alice", "other");
        var content = new PackageContent(new("Echo", "Echo text", [new("ask", "Echo input")], []), "// behavior");
        foreach (var packageId in new[] { id, other })
        {
            var package = brain.Get<IPackage>(packageId.ToString());
            var revision = await package.Commit(new(Guid.NewGuid(), null, content, "Initial"));
            await package.Publish(new(Guid.NewGuid(), revision.Id));
            await brain.Get<IApp>(scope + "/packages/" + packageId).Install(new(Guid.NewGuid(), new(packageId, revision.Id), new Dictionary<string, string>()));
        }
        var names = new[] { AppToolName.For(id, "ask"), AppToolName.For(other, "ask") };
        Assert.Equal(2, names.Distinct().Count());
        var callId = "";
        var source = Assert.Single(brain.SiloServices.GetServices<IAgentToolSource>());
        await using var session = await source.OpenAsync(names, () => new(scope, "run", callId), ct);
        var tool = Assert.Single(session.Tools, tool => tool.Name == names[0]);
        callId = "call-1";
        var invocation = tool.InvokeAsync(new AIFunctionArguments { ["input"] = "hello" }, deadline.Token).AsTask();
        var app = brain.Get<IApp>(scope + "/packages/" + id);
        AppInvocation? pending = null;
        while (pending is null)
        {
            pending = (await app.Pending().WaitAsync(deadline.Token)).SingleOrDefault();
            if (pending is null) { await Task.Delay(10, deadline.Token); }
        }
        Assert.Equal("hello", pending.Input);
        Assert.Empty(await brain.Get<IApp>(scope + "/packages/" + other).Pending());
        await app.Respond(new(pending.Id, "Hello", fail ? "Resource unavailable" : null));
        var result = Assert.IsType<JsonElement>(await invocation);
        if (fail)
        {
            Assert.True(result.GetProperty("isError").GetBoolean());
            Assert.Equal("app_failed", result.GetProperty("code").GetString());
            Assert.Equal("Resource unavailable", result.GetProperty("message").GetString());
        }
        else { Assert.Equal("Hello", result.GetProperty("output").GetString()); }
        var replayed = Assert.IsType<JsonElement>(await tool.InvokeAsync(new AIFunctionArguments { ["input"] = "hello" }, deadline.Token));
        Assert.Equal(result.GetProperty("id").GetGuid(), replayed.GetProperty("id").GetGuid());
        Assert.Empty(await app.Pending());
    }
}
