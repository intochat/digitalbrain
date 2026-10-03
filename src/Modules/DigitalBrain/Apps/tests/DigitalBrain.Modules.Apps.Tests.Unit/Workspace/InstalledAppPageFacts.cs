using DigitalBrain.Apps;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Kernel.Enforcement;
using DigitalBrain.Registry;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;
using Orleans.Storage;

namespace DigitalBrain.Modules.Apps.Tests.Unit.Workspace;

public sealed class InstalledAppPageFacts
{
    [Fact]
    public async Task PagingBackfillsALegacyInstallationWithoutASeparateListCall()
    {
        var ct = TestContext.Current.CancellationToken;
        var storage = new FlakyGrainStorage();
        await using var brain = await UnitTest.Create().WithModule<AppsModule>().WithModule<RegistryModule>()
            .ConfigureSilo(silo => silo.Services.AddKeyedSingleton<IGrainStorage>("Default", storage)).StartAsync(ct);
        Caller.As("alice");
        await brain.AuthorizeCallerAsync();
        var scope = BrainScope.CurrentId();
        var id = PackageId.Create("alice", "legacy");
        var revision = await brain.Get<IPackage>(id.ToString()).Commit(new(Guid.NewGuid(), null,
            new(new("Legacy", "Legacy installation", [], [], Runtime: "prompt"), ""), "Initial"));
        var app = brain.Get<IApp>(scope + "/packages/" + id);
        await app.Install(new(Guid.NewGuid(), new(id, revision.Id), new Dictionary<string, string>()));
        CallerContextStamper.Stamp(CallerContextStamper.Require() with { Kind = CallerKind.Platform, StampedBy = TrustedEdge.Platform });
        var registry = brain.Get<IRegistry>(IRegistry.Key);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (!(await registry.Instances("apps.app")).Any(item => item.Key == scope + "/packages/" + id))
        { await Task.Delay(20, timeout.Token); }
        var index = brain.Get<IApps>(scope);
        await brain.DeactivateAsync(index, ct);
        await storage.ClearStateAsync("installed-apps", index.GetGrainId(), new GrainState<InstalledAppsState>());
        Caller.As("alice");
        var page = await index.Page(0, 10);
        Assert.True(page.Ready);
        Assert.Equal(id, Assert.Single(page.Packages));
        Assert.Equal("apps:" + id, Assert.Single((await registry.Browse("", "apps", cancellationToken: ct)).Capabilities).Id);
    }
}
