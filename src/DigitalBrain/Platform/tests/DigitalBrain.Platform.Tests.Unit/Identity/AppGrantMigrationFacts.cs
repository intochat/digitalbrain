using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Kernel.Enforcement;
using DigitalBrain.Platform.Contracts.Identity;
using DigitalBrain.Platform.Identity.Authority;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Storage;

namespace DigitalBrain.Platform.Tests.Unit.Identity;

public sealed class AppGrantMigrationFacts
{
    [Fact]
    public async Task MigrationPreservesGrantModesConversationsAndRevocationsWithoutReissuingConsumedGrants()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await ModuleTest.Create().StartAsync(ct);
        var authority = brain.Get<IBrainAuthority>(BrainScope.Create("account", "brain").Id);
        await authority.Initialize("account", "brain");
        var always = await authority.Grant(Grant("file-one", "email", GrantMode.Always));
        var chatOne = await authority.Grant(Grant("file-two", "email", GrantMode.ThisChat) with { ConversationId = "chat-one" });
        var chatTwo = await authority.Grant(Grant("file-one", "email", GrantMode.ThisChat) with { ConversationId = "chat-two" });
        await authority.Grant(Grant("file-one", "spent", GrantMode.Once));
        Assert.True((await authority.Authorize(Request("file-one", "spent")))!.Allowed);
        await authority.Grant(Grant("file-two", "revoked", GrantMode.Always) with { Revoked = true });
        await authority.Grant(Grant("file-one", "collision", GrantMode.Always) with { Revoked = true });
        await authority.Grant(Grant("file-two", "collision", GrantMode.Always));
        await authority.Grant(Grant("file-two", "once", GrantMode.Once));
        var other = await authority.Grant(Grant("another-app", "email", GrantMode.Always));
        Stamp();
        var host = brain.Get<IAppGrantMigrationTestHost>("stable-app");
        await host.Migrate(["file-one", "file-two"]);
        var grants = await authority.ListGrants();
        Assert.Contains(always with { AppId = "stable-app" }, grants);
        Assert.Contains(chatOne with { AppId = "stable-app" }, grants);
        Assert.Contains(chatTwo with { AppId = "stable-app" }, grants);
        Assert.Contains(other, grants);
        Assert.DoesNotContain(grants, g => g.SemanticTypeId is "spent" or "revoked" or "collision");
        Assert.True((await authority.Authorize(Request("stable-app", "once")))!.Allowed);
        await authority.Revoke("stable-app", "email", GrantMode.Always);
        // A stale owner action referring to a retired file ID cannot be imported on retry.
        await authority.Grant(Grant("file-one", "email", GrantMode.Always));
        await host.Migrate(["file-one", "file-two"]);
        Assert.DoesNotContain(await authority.ListGrants(), g => g.AppId == "stable-app" && (g.Mode == GrantMode.Once || g.Mode == GrantMode.Always));
    }

    [Fact]
    public async Task AGrantMoveWhoseSavedResponseWasLostResumesWithoutRestoringLaterRevocations()
    {
        await using var brain = await ModuleTest.Create().ConfigureSilo(silo =>
            silo.Services.AddKeyedSingleton<IGrainStorage>(DigitalBrain.Contracts.DigitalBrainNames.DefaultGrainStorage,
                (services, _) => new LostMigrationResponse(new OrleansGrainStorageSerializer(services.GetRequiredService<Serializer>()))))
            .StartAsync(TestContext.Current.CancellationToken);
        var authority = brain.Get<IBrainAuthority>(BrainScope.Create("account", "brain").Id);
        await authority.Initialize("account", "brain");
        await authority.Grant(Grant("legacy-file", "email", GrantMode.Always));
        Stamp();
        var host = brain.Get<IAppGrantMigrationTestHost>("stable-app");
        await Assert.ThrowsAnyAsync<Exception>(() => host.Migrate(["legacy-file"]));
        Assert.Equal("stable-app", Assert.Single(await authority.ListGrants()).AppId);
        await authority.Revoke("stable-app", "email", GrantMode.Always);
        await host.Migrate(["legacy-file"]);
        Assert.Empty(await authority.ListGrants());
    }

    private sealed class LostMigrationResponse(IGrainStorageSerializer serializer) : IGrainStorage
    {
        private readonly Dictionary<(string, GrainId), BinaryData> saved = [];
        private bool failed;
        public Task ReadStateAsync<T>(string name, GrainId id, IGrainState<T> state)
        {
            if (saved.TryGetValue((name, id), out var value)) { state.State = serializer.Deserialize<T>(value); state.RecordExists = true; }
            return Task.CompletedTask;
        }
        public Task WriteStateAsync<T>(string name, GrainId id, IGrainState<T> state)
        {
            saved[(name, id)] = serializer.Serialize(state.State);
            state.RecordExists = true;
            if (!failed && state.State is BrainAuthorityState { AppGrantMigrations.Length: > 0 })
            { failed = true; throw new IOException("Saved the move, but lost its response."); }
            return Task.CompletedTask;
        }
        public Task ClearStateAsync<T>(string name, GrainId id, IGrainState<T> state)
        { saved.Remove((name, id)); return Task.CompletedTask; }
    }

    [Fact]
    public async Task OnlyTheOriginatingAppsGrainCanMigrateItsCurrentBrainGrants()
    {
        await using var brain = await ModuleTest.Create().StartAsync(TestContext.Current.CancellationToken);
        Stamp();
        var migration = brain.Get<IAppGrantMigration>(BrainScope.CurrentId());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => migration.Migrate("stable-app", ["file"]));
        var host = brain.Get<IAppGrantMigrationTestHost>("stable-app");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => host.Migrate(["file"], "victim-app"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => host.Migrate(["file"], authority: BrainScope.Create("account", "other-brain").Id));
    }

    private static Grant Grant(string app, string semantic, GrantMode mode)
        => new() { AppId = app, WorkspaceId = "brain", SemanticTypeId = semantic, Mode = mode };

    private static CallRequest Request(string app, string semantic)
        => new()
        {
            Caller = new() { PrincipalId = "alice", AccountId = "account", BrainId = "brain", Kind = CallerKind.App, StampedBy = TrustedEdge.AppProxy, AppId = app },
            SemanticTypeIds = [semantic],
            TargetNeuron = "target",
            Operation = "Read"
        };

    private static void Stamp() => CallerContextStamper.Stamp(new()
    {
        PrincipalId = "alice",
        AccountId = "account",
        BrainId = "brain",
        Kind = CallerKind.User,
        StampedBy = TrustedEdge.AuthenticatedHttp
    });
}

public interface IAppGrantMigrationTestHost : IGrainWithStringKey
{
    Task Migrate(string[] files, string? app = null, string? authority = null);
}

[GrainType("apps.app")]
public sealed class AppGrantMigrationTestHost : Grain, IAppGrantMigrationTestHost
{
    public Task Migrate(string[] files, string? app = null, string? authority = null)
        => GrainFactory.GetGrain<IAppGrantMigration>(authority ?? BrainScope.CurrentId()).Migrate(app ?? this.GetPrimaryKeyString(), files);
}
