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
    public async Task PendingAppToolReturnsAnErrorWithoutExternalCancellation()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<AppsModule>().StartAsync(ct);
        Caller.As("alice");
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
        Assert.Single(await app.Pending());
    }

    [Fact]
    public async Task AppToolsInvokeTheirOwnAppAndReplayTheSameCallWithoutInvokingAgain()
    {
        var ct = TestContext.Current.CancellationToken;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        await using var brain = await UnitTest.Create().WithModule<AppsModule>().StartAsync(ct);
        Caller.As("alice");
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
        await app.Respond(new(pending.Id, "Hello", null));
        var result = Assert.IsType<JsonElement>(await invocation);
        Assert.Equal("Hello", result.GetProperty("output").GetString());
        var replayed = Assert.IsType<JsonElement>(await tool.InvokeAsync(new AIFunctionArguments { ["input"] = "hello" }, deadline.Token));
        Assert.Equal(result.GetProperty("id").GetGuid(), replayed.GetProperty("id").GetGuid());
        Assert.Empty(await app.Pending());
    }
}
