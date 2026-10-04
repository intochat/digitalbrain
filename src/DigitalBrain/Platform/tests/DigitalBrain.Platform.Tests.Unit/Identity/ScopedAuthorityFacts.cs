using DigitalBrain.Kernel.Enforcement;
using DigitalBrain.Platform.Contracts.Identity;
using DigitalBrain.Platform.Identity.Authority;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Storage;

namespace DigitalBrain.Platform.Tests.Unit.Identity;

public sealed class ScopedAuthorityFacts
{
    [Fact]
    public async Task RegistrationResumesAfterProvisioningFailsWithoutAllocatingAnotherAccount()
    {
        await using var brain = await ModuleTest.Create().ConfigureSilo(silo =>
            silo.Services.AddKeyedSingleton<IGrainStorage>(DigitalBrain.Contracts.DigitalBrainNames.DefaultGrainStorage,
                (services, _) => new FailOnceStorage(new OrleansGrainStorageSerializer(services.GetRequiredService<Serializer>()))))
            .StartAsync(TestContext.Current.CancellationToken);
        var principal = brain.Get<IPrincipal>("recoverable");
        var failure = await Assert.ThrowsAsync<OrleansException>(() => principal.Register("long-test-password", "Recoverable"));
        Assert.IsType<IOException>(failure.InnerException);
        var allocated = await principal.DefaultMembership();
        Assert.NotNull(allocated);
        Assert.Null(await principal.Authenticate("wrong-password"));
        var resumed = await principal.Register("long-test-password", "Recoverable");
        Assert.Equal(allocated, resumed);
        Assert.Equal(resumed, await principal.Authenticate("long-test-password"));
        Assert.NotNull(await brain.Get<IBrainAuthority>(BrainScope.Create(resumed.AccountId, resumed.BrainId).Id).Membership("recoverable"));
    }

    private sealed class FailOnceStorage(IGrainStorageSerializer serializer) : IGrainStorage
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<(string, GrainId), BinaryData> _data = new();
        private int _failed;
        public Task ReadStateAsync<T>(string name, GrainId id, IGrainState<T> state)
        {
            if (_data.TryGetValue((name, id), out var bytes)) { state.State = serializer.Deserialize<T>(bytes); state.RecordExists = true; }
            return Task.CompletedTask;
        }
        public Task WriteStateAsync<T>(string name, GrainId id, IGrainState<T> state)
        {
            if (typeof(T) == typeof(AccountState) && Interlocked.Exchange(ref _failed, 1) == 0)
            { throw new IOException("Interrupted account provisioning."); }
            _data[(name, id)] = serializer.Serialize(state.State);
            state.RecordExists = true;
            return Task.CompletedTask;
        }
        public Task ClearStateAsync<T>(string name, GrainId id, IGrainState<T> state)
        { _data.TryRemove((name, id), out _); return Task.CompletedTask; }
    }

    [Fact]
    public async Task RevokedOwnerCannotMutateGrantsWithAnOldSession()
    {
        await using var brain = await ModuleTest.Create().StartAsync(TestContext.Current.CancellationToken);
        var authority = brain.Get<IBrainAuthority>(BrainScope.Create("account", "brain").Id);
        await authority.EnsureMember(new Member { PrincipalId = "alice", AccountId = "account", BrainId = "brain", DisplayName = "Alice", Role = MemberRole.Owner });
        var caller = new DigitalBrain.Contracts.Enforcement.CallerContext
        {
            PrincipalId = "alice",
            AccountId = "account",
            BrainId = "brain",
            Kind = DigitalBrain.Contracts.Enforcement.CallerKind.User,
            StampedBy = DigitalBrain.Contracts.Enforcement.TrustedEdge.AuthenticatedHttp
        };
        var grant = new Grant { AppId = "app", WorkspaceId = "brain", SemanticTypeId = "person.email", Mode = GrantMode.Always };
        await authority.GrantAsOwner(caller, grant);
        await authority.RemoveMember("alice");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => authority.GrantAsOwner(caller, grant));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => authority.RevokeAsOwner(caller, "app", "person.email", GrantMode.Always));
    }

    [Fact]
    public async Task BasicBootstrapOwnsOnlyItsExplicitDefaultScope()
    {
        await using var brain = await ModuleTest.Create().ConfigureSilo(silo =>
            silo.Services.Configure<DigitalBrain.Platform.Identity.Configuration.AuthOptions>(options =>
            {
                options.Posture = DigitalBrain.Platform.Identity.Configuration.IdentityPosture.Secured;
                options.Username = "operator";
                options.Password = "configured-bootstrap-password";
            })).StartAsync(TestContext.Current.CancellationToken);
        var access = brain.SiloServices.GetRequiredService<IBrainAccess>();
        Assert.True(await access.CanAccessAsync("operator", "operator", "owner", TestContext.Current.CancellationToken));
        Assert.False(await access.CanAccessAsync("operator", "operator", "another-brain", TestContext.Current.CancellationToken));
        Assert.False(await access.CanAccessAsync("operator", "another-account", "owner", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SameBrainNameInAnotherAccountDoesNotShareMembership()
    {
        await using var brain = await ModuleTest.Create().StartAsync(TestContext.Current.CancellationToken);
        var first = brain.Get<IBrainAuthority>(BrainScope.Create("first", "shared-name").Id);
        var second = brain.Get<IBrainAuthority>(BrainScope.Create("second", "shared-name").Id);
        await first.EnsureMember(new Member { PrincipalId = "alice", AccountId = "first", BrainId = "shared-name", DisplayName = "Alice", Role = MemberRole.Owner });
        await second.Initialize("second", "shared-name");
        Assert.NotNull(await first.Membership("alice"));
        Assert.Null(await second.Membership("alice"));
    }

    [Fact]
    public async Task RetryingBrainCreationKeepsItsIdAndDoesNotRestoreRevokedMembership()
    {
        await using var brain = await ModuleTest.Create().StartAsync(TestContext.Current.CancellationToken);
        var member = await brain.Get<IPrincipal>("alice").Register("long-test-password", "Alice");
        var account = brain.Get<IAccount>(member.AccountId);
        var first = await account.CreateBrain("alice", "operation-1", "First");
        var authority = brain.Get<IBrainAuthority>(BrainScope.Create(first.AccountId, first.BrainId).Id);
        await authority.RemoveMember("alice");
        Assert.Equal(first, await account.CreateBrain("alice", "operation-1", "First"));
        Assert.Null(await authority.Membership("alice"));
        Assert.NotEqual(first.BrainId, (await account.CreateBrain("alice", "operation-2", "Second")).BrainId);
    }

    [Fact]
    public async Task RepeatingAnImportCannotRestoreRevokedGrantsOrMembers()
    {
        await using var brain = await ModuleTest.Create().StartAsync(TestContext.Current.CancellationToken);
        var authority = brain.Get<IBrainAuthority>(BrainScope.Create("account", "brain").Id);
        var member = new Member { PrincipalId = "alice", AccountId = "account", BrainId = "brain", DisplayName = "Alice", Role = MemberRole.Owner };
        var grant = new Grant { AppId = "app", WorkspaceId = "brain", SemanticTypeId = "person.email", Mode = GrantMode.Always };
        await authority.Initialize("account", "brain");
        await authority.Import([member], [grant], []);
        await authority.RemoveMember("alice");
        await authority.Revoke("app", "person.email", GrantMode.Always);
        await authority.Import([member], [grant], []);
        Assert.Null(await authority.Membership("alice"));
        Assert.Empty(await authority.ListGrants());
    }
}
