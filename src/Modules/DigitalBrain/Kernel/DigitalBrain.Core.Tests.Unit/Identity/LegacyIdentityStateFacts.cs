using DigitalBrain.Contracts;
using DigitalBrain.Identity;
using DigitalBrain.Identity.Directory;
using DigitalBrain.Identity.Grants;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Storage;

namespace DigitalBrain.Core.Tests.Unit.Identity;

public sealed class LegacyIdentityStateFacts
{
    [Fact]
    public async Task IdentityStateWrittenBeforeTheAssemblyMoveLoadsAndSurvivesReactivation()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().ConfigureSilo(silo =>
        {
            // The persisted Azure Blob provider uses this serializer in AddDigitalBrainRuntime.
            silo.Services.AddKeyedSingleton<IGrainStorage>(DigitalBrainNames.DefaultGrainStorage, (services, _) =>
                new LegacyStorage(new OrleansGrainStorageSerializer(services.GetRequiredService<Serializer>())));
        }).StartAsync(ct);
        var directory = brain.Get<IIdentityDirectory>(IdentityGrains.Directory);
        var grants = brain.Get<IGrantStore>(IdentityGrains.Grants("legacy-brain"));
        var member = await directory.AuthenticateAsync("alice", "legacy-password", ct);
        Assert.NotNull(member);
        Assert.Equal("legacy-account", member.AccountId);
        Assert.True(await directory.CanAccessAsync("alice", "legacy-brain", ct));
        var grant = Assert.Single(await grants.ListAsync(ct));
        Assert.Equal("legacy-app", grant.AppId);
        Assert.Equal("person.email", grant.SemanticTypeId);
        Assert.Equal(GrantMode.ThisChat, grant.Mode);
        Assert.Equal("legacy-chat", grant.ConversationId);
        Assert.Equal(DateTimeOffset.Parse("2026-09-30T12:00:00Z"), grant.GrantedAt);

        await directory.ShareBrainAsync("legacy-account", "shared-brain", "bob", "Bob", MemberRole.Member, ct);
        await grants.GrantAsync(grant with { SemanticTypeId = "person.name", Mode = GrantMode.Always }, ct);
        await brain.DeactivateAsync(directory.AsReference<INeuron>(), ct);
        await brain.DeactivateAsync(grants.AsReference<INeuron>(), ct);

        Assert.Equal(member, await directory.AuthenticateAsync("alice", "legacy-password", ct));
        Assert.True(await directory.CanAccessAsync("bob", "shared-brain", ct));
        Assert.Equal(2, (await grants.ListAsync(ct)).Count);
        Assert.Contains(grant, await grants.ListAsync(ct));
        Assert.DoesNotContain(AppDomain.CurrentDomain.GetAssemblies(), assembly =>
            assembly.GetName().Name is "DigitalBrain.Modules.Identity" or "DigitalBrain.Modules.Identity.Contracts");
        var storage = Assert.IsType<LegacyStorage>(brain.SiloServices.GetRequiredKeyedService<IGrainStorage>(DigitalBrainNames.DefaultGrainStorage));
        Assert.True(storage.Reads >= 4);
    }

    private sealed class LegacyStorage(IGrainStorageSerializer serializer) : IGrainStorage
    {
        private readonly Dictionary<(string, GrainId), BinaryData> _written = [];
        public int Reads { get; private set; }

        public async Task ReadStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> state)
        {
            Reads++;
            if (!_written.TryGetValue((stateName, grainId), out var data))
            {
                var fixture = typeof(T) == typeof(IdentityDirectoryState) ? "directory"
                    : typeof(T) == typeof(GrantStoreState) ? "grants" : null;
                if (fixture is null) { return; }
                data = new BinaryData(await File.ReadAllBytesAsync(
                    Path.Combine(AppContext.BaseDirectory, "Identity", "LegacyState", fixture + ".orleans"),
                    TestContext.Current.CancellationToken));
            }
            state.State = serializer.Deserialize<T>(data);
            state.RecordExists = true;
            state.ETag = "legacy";
        }

        public Task WriteStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> state)
        {
            _written[(stateName, grainId)] = serializer.Serialize(state.State);
            state.RecordExists = true;
            state.ETag = "updated";
            return Task.CompletedTask;
        }

        public Task ClearStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> state)
            => throw new NotSupportedException();
    }
}
