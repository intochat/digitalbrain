using DigitalBrain.Apps;
using DigitalBrain.Assistant;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Core.Enforcement;
using DigitalBrain.AI.Agents;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Modules.Assistant.Tests.Unit;

public sealed class AgentToolSelectionFacts
{
    [Fact]
    public async Task ToolsComeFromTheInstalledRevisionInTheCurrentBrain()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<AppsModule>().StartAsync(ct);
        CallerContextStamper.Stamp(new CallerContext
        {
            PrincipalId = "alice",
            AccountId = "alice",
            BrainId = "personal",
            Kind = CallerKind.User,
            StampedBy = TrustedEdge.AuthenticatedHttp,
        });
        var id = PackageId.Create("alice", "tools");
        var package = brain.Get<IPackage>(id.ToString());
        var content = new PackageContent(new("Tools", "Installed tools", [new("old-tool", "Old")], [], Runtime: "prompt"), "");
        var published = await package.Commit(new(Guid.NewGuid(), null, content, "Initial"));
        await package.Publish(new(Guid.NewGuid(), published.Id));
        var revision = await package.Commit(new(Guid.NewGuid(), published.Id,
            content with { Manifest = content.Manifest with { Operations = [new("installed-tool", "Installed")] } }, "Change tools"));
        var scope = BrainScope.CurrentId();
        var installed = brain.Get<IApp>(scope + "/packages/" + id);
        await installed.Install(new(Guid.NewGuid(), new(id, revision.Id), new Dictionary<string, string>()));
        var selection = new AgentToolSelection(brain.Grains);

        Assert.Equal([AppToolName.For(id, "installed-tool")], await selection.ResolveAsync(scope, ct));
        var selected = await selection.ResolveAsync(scope, ct);
        var source = Assert.Single(brain.SiloServices.GetServices<IAgentToolSource>());
        await using (var session = await source.OpenAsync(selected, () => new(scope, "run", ""), ct))
        {
            Assert.Equal(selected, session.Tools.Select(tool => tool.Name));
        }
        // Resolve the selected tools in the real runner before it reaches the model.
        using var services = new ServiceCollection().AddSingleton(source)
            .AddSingleton<IChatClient>(new ScriptedAssistantModel()).BuildServiceProvider();
        var events = new List<AgentTurnEvent>();
        await foreach (var item in new AgentTurnRunner(services).RunAsync(new("assistant", "run", scope, [], "hello", null, ToolNames: selected), ct))
        { events.Add(item); }
        Assert.DoesNotContain(events, item => item is AgentTurnEvent.Failed);
        Assert.Contains(events, item => item is AgentTurnEvent.Completed);
        Assert.Empty(await selection.ResolveAsync(BrainScope.Create("alice", "other").Id, ct));
        await installed.Uninstall(new(Guid.NewGuid()));
        Assert.Empty(await selection.ResolveAsync(scope, ct));
    }

    [Fact]
    public async Task PrivateInstallsAreImmediatelyDiscoverableWithoutTheOptionalRegistry()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<AppsModule>().StartAsync(ct);
        CallerContextStamper.Stamp(new CallerContext
        {
            PrincipalId = "alice",
            AccountId = "alice",
            BrainId = "personal",
            Kind = CallerKind.User,
            StampedBy = TrustedEdge.AuthenticatedHttp,
        });
        var scope = BrainScope.CurrentId();
        var id = PackageId.Create("alice", "private");
        var content = new PackageContent(new("Private", "A private app", [new("ask", "Answer")], [], Runtime: "prompt"), "");
        var revision = await brain.Get<IPackage>(id.ToString()).Commit(new(Guid.NewGuid(), null, content, "Private"));
        var app = brain.Get<IApp>(scope + "/packages/" + id);
        await app.Install(new(Guid.NewGuid(), new(id, revision.Id), new Dictionary<string, string>()));
        Assert.Empty(await brain.Get<IPackageDirectory>(PackageDirectory.Key).List());
        Assert.Equal([AppToolName.For(id, "ask")], await new AgentToolSelection(brain.Grains).ResolveAsync(scope, ct));
        await brain.DeactivateAsync(app, ct);
        await brain.DeactivateAsync(brain.Get<IApps>(scope), ct);
        Assert.Equal(id, Assert.Single(await brain.Get<IApps>(scope).List()));
        Assert.Equal(id.ToString(), Assert.Single(await InstalledApps.List(brain, scope, ct)).Id);
    }
}
