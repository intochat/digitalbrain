using DigitalBrain.AI.Agents;
using DigitalBrain.Apps;
using DigitalBrain.Assistant;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Kernel.Enforcement;
using DigitalBrain.Registry;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Modules.Assistant.Tests;

public sealed class AgentToolSelectionFacts
{
    [Fact]
    public async Task ToolsComeFromTheInstalledRevisionInTheCurrentBrain()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await ModuleTest.Create().WithModule<AppsModule>().WithModule<RegistryModule>().StartAsync(ct);
        CallerContextStamper.Stamp(new CallerContext
        {
            PrincipalId = "alice",
            AccountId = "alice",
            BrainId = "personal",
            Kind = CallerKind.User,
            StampedBy = TrustedEdge.AuthenticatedHttp,
        });
        await brain.AuthorizeCallerAsync();
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
        var registry = brain.Get<IRegistry>(IRegistry.Key);
        async Task<string[]> Discover() => (await registry.Select("apps:" + id, ct))?.Tools ?? [];

        Assert.Equal([AppToolName.For(id, "installed-tool")], await Discover());
        var selected = await Discover();
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
        // Discovery has no model-controlled scope argument; the Registry tests cover caller isolation.
        await installed.Uninstall(new(Guid.NewGuid()));
        Assert.Empty(await Discover());
    }

    [Fact]
    public async Task PrivateInstallsAreDiscoverableWithoutPublicationOrActivationHistory()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await ModuleTest.Create().WithModule<AppsModule>().WithModule<RegistryModule>().StartAsync(ct);
        CallerContextStamper.Stamp(new CallerContext
        {
            PrincipalId = "alice",
            AccountId = "alice",
            BrainId = "personal",
            Kind = CallerKind.User,
            StampedBy = TrustedEdge.AuthenticatedHttp,
        });
        await brain.AuthorizeCallerAsync();
        var scope = BrainScope.CurrentId();
        var id = PackageId.Create("alice", "private");
        var content = new PackageContent(new("Private", "A private app", [new("ask", "Answer")], [], Runtime: "prompt"), "");
        var revision = await brain.Get<IPackage>(id.ToString()).Commit(new(Guid.NewGuid(), null, content, "Private"));
        var app = brain.Get<IApp>(scope + "/packages/" + id);
        await app.Install(new(Guid.NewGuid(), new(id, revision.Id), new Dictionary<string, string>()));
        Assert.Empty(await brain.Get<IPackageDirectory>(PackageDirectory.Key).List());
        Assert.Equal([AppToolName.For(id, "ask")], (await brain.Get<IRegistry>(IRegistry.Key).Select("apps:" + id, ct))!.Tools);
        await brain.DeactivateAsync(app, ct);
        await brain.DeactivateAsync(brain.Get<IApps>(scope), ct);
        Assert.Equal(id, Assert.Single(await brain.Get<IApps>(scope).List()));
        Assert.Equal(id.ToString(), Assert.Single(await InstalledApps.List(brain, scope, ct)).Id);
    }
}
