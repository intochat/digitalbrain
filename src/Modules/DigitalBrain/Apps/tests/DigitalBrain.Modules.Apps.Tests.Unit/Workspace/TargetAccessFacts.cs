using DigitalBrain.Apps;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Kernel.Enforcement;

namespace DigitalBrain.Modules.Apps.Tests.Unit.Workspace;

public sealed class TargetAccessFacts
{
    [Fact]
    public async Task PublicContractsImplementedInPlatformStillEnforceTheirTargetBrain()
    {
        await using var brain = await ModuleTest.Create().StartAsync(TestContext.Current.CancellationToken);
        CallerContextStamper.Stamp(new() { PrincipalId = "owner", AccountId = "owner", BrainId = "alice", Kind = CallerKind.User, StampedBy = TrustedEdge.AuthenticatedHttp });
        try
        {
            var foreign = brain.Get<DigitalBrain.Contracts.Integrations.IConnectionRequests>(BrainScope.Create("owner", "bob").Id);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(foreign.ListPending);
            var own = brain.Get<DigitalBrain.Contracts.Integrations.IConnectionRequests>(BrainScope.CurrentId());
            Assert.Empty(await own.ListPending());
        }
        finally { Caller.Clear(); }
    }

    [Fact]
    public async Task SelectedBrainCannotReadOrCreateAForeignAppThroughRawGrainCalls()
    {
        await using var brain = await ModuleTest.Create().WithModule<AppsModule>().StartAsync(TestContext.Current.CancellationToken);
        CallerContextStamper.Stamp(new() { PrincipalId = "owner", AccountId = "owner", BrainId = "alice", Kind = CallerKind.User, StampedBy = TrustedEdge.AuthenticatedHttp });
        try
        {
            var foreign = brain.Get<IApp>(BrainScope.Create("owner", "bob").Id + "/packages/owner/example");
            await Assert.ThrowsAsync<UnauthorizedAccessException>(foreign.Read);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => foreign.Uninstall(new(Guid.NewGuid())));
            var own = brain.Get<IApp>(BrainScope.CurrentId() + "/packages/owner/example");
            Assert.Equal(AppStatus.NotInstalled, (await own.Read()).Status);
        }
        finally { Caller.Clear(); }
    }

    [Fact]
    public async Task PublicPackageReadDoesNotMakePackageMutationPublic()
    {
        await using var brain = await ModuleTest.Create().WithModule<AppsModule>().StartAsync(TestContext.Current.CancellationToken);
        CallerContextStamper.Stamp(new() { PrincipalId = "owner", AccountId = "owner", BrainId = "alice", Kind = CallerKind.User, StampedBy = TrustedEdge.AuthenticatedHttp });
        try
        {
            var package = brain.Get<IPackage>("someone/example");
            Assert.Null((await package.Read()).Head);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => package.Commit(new(Guid.NewGuid(), null,
                new PackageContent(new("Example", "Example", [], [], Runtime: "prompt"), ""), "Create")));
        }
        finally { Caller.Clear(); }
    }
}
