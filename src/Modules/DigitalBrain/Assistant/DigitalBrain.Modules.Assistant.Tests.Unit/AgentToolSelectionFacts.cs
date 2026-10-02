using DigitalBrain.Apps;
using DigitalBrain.Assistant;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Core.Enforcement;

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
            PrincipalId = "alice", AccountId = "alice", BrainId = "personal",
            Kind = CallerKind.User, StampedBy = TrustedEdge.AuthenticatedHttp,
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

        Assert.Equal(["installed-tool"], await selection.ResolveAsync(scope, ct));
        Assert.Empty(await selection.ResolveAsync(BrainScope.Create("alice", "other").Id, ct));
        await installed.Uninstall(new(Guid.NewGuid()));
        Assert.Empty(await selection.ResolveAsync(scope, ct));
    }
}
