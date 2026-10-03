using DigitalBrain;
using DigitalBrain.Contracts;
using DigitalBrain.Platform.Contracts.Identity;
using DigitalBrain.Platform.Identity.Directory;
using DigitalBrain.Platform.Identity.Grants;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Storage;

namespace DigitalBrain.Platform.Tests.Unit.Identity;

public sealed class LegacyIdentityStateFacts
{
    [Fact]
    public async Task IncompleteGrantOnlyMigrationBlocksNormalUseEvenWithoutDirectoryRecords()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().ConfigureSilo(silo =>
        {
            silo.Services.Configure<IdentityMigrationOptions>(options => options.Maintenance = true);
            silo.Services.AddKeyedSingleton<IGrainStorage>(DigitalBrainNames.DefaultGrainStorage, (services, _) =>
                new LegacyStorage(new OrleansGrainStorageSerializer(services.GetRequiredService<Serializer>()), unfinishedGrantOnly: true));
        }).StartAsync(ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => brain.Get<IIdentityDirectory>(IdentityGrains.Directory).PrepareAsync(ct));
    }

    [Fact]
    public async Task CompletedCheckpointWithoutPlanIdNeverReimports()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().ConfigureSilo(silo =>
        {
            silo.Services.Configure<IdentityMigrationOptions>(options => options.Maintenance = true);
            silo.Services.AddKeyedSingleton<IGrainStorage>(DigitalBrainNames.DefaultGrainStorage, (services, _) =>
                new LegacyStorage(new OrleansGrainStorageSerializer(services.GetRequiredService<Serializer>()), alreadyCompleted: true));
        }).StartAsync(ct);
        var provider = Assert.IsType<LegacyStorage>(brain.SiloServices.GetRequiredKeyedService<IGrainStorage>(DigitalBrainNames.DefaultGrainStorage));
        var writes = provider.Writes;
        await brain.Get<IIdentityDirectory>(IdentityGrains.Directory).ApplyMigrationAsync("new-plan-must-not-reimport", ct);
        Assert.Equal(writes, provider.Writes);
    }

    [Fact]
    public async Task IdentityStateWrittenBeforeTheAssemblyMoveLoadsAndSurvivesReactivation()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().ConfigureSilo(silo =>
        {
            silo.Services.Configure<IdentityMigrationOptions>(options => { options.Maintenance = true; options.SourceSnapshotId = "legacy-fixtures"; options.LegacyGrantBrainIds = ["legacy-brain"]; });
            // The persisted Azure Blob provider uses this serializer in AddDigitalBrainRuntime.
            silo.Services.AddKeyedSingleton<IGrainStorage>(DigitalBrainNames.DefaultGrainStorage, (services, _) =>
                new LegacyStorage(new OrleansGrainStorageSerializer(services.GetRequiredService<Serializer>())));
        }).StartAsync(ct);
        var directory = brain.Get<IIdentityDirectory>(IdentityGrains.Directory);
        var provider = Assert.IsType<LegacyStorage>(brain.SiloServices.GetRequiredKeyedService<IGrainStorage>(DigitalBrainNames.DefaultGrainStorage));
        var initialWrites = provider.Writes;
        await Assert.ThrowsAsync<InvalidOperationException>(() => directory.PrepareAsync(ct));
        var planId = await directory.InspectMigrationAsync(ct);
        Assert.Equal(initialWrites, provider.Writes);
        await Assert.ThrowsAsync<InvalidOperationException>(() => directory.ApplyMigrationAsync("changed-plan", ct));
        Assert.Equal(initialWrites, provider.Writes);
        await directory.ApplyMigrationAsync(planId, ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => directory.ApplyMigrationAsync("changed-plan", ct));
        var grants = brain.Get<IGrantStore>(IdentityGrains.Grants("legacy-brain"));
        var member = await directory.AuthenticateAsync("alice", "legacy-password", ct);
        Assert.NotNull(member);
        Assert.Equal("legacy-account", member.AccountId);
        Assert.True(await directory.CanAccessAsync("alice", "legacy-account", "legacy-brain", ct));
        var grant = Assert.Single(await grants.ListAsync(ct));
        Assert.Equal("legacy-app", grant.AppId);
        Assert.Equal("person.email", grant.SemanticTypeId);
        Assert.Equal(GrantMode.ThisChat, grant.Mode);
        Assert.Equal("legacy-chat", grant.ConversationId);
        Assert.Equal(DateTimeOffset.Parse("2026-09-30T12:00:00Z"), grant.GrantedAt);

        await directory.ShareBrainAsync("legacy-account", "shared-brain", "bob", "Bob", MemberRole.Member, ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => grants.GrantAsync(grant with { SemanticTypeId = "person.name", Mode = GrantMode.Always }, ct));
        await brain.DeactivateAsync(directory.AsReference<INeuron>(), ct);
        await brain.DeactivateAsync(grants.AsReference<INeuron>(), ct);

        Assert.Equal(member, await directory.AuthenticateAsync("alice", "legacy-password", ct));
        Assert.True(await directory.CanAccessAsync("bob", "legacy-account", "shared-brain", ct));
        Assert.Single(await grants.ListAsync(ct));
        Assert.Contains(grant, await grants.ListAsync(ct));
        Assert.DoesNotContain(AppDomain.CurrentDomain.GetAssemblies(), assembly =>
            assembly.GetName().Name is "DigitalBrain.Modules.Identity" or "DigitalBrain.Modules.Identity.Contracts");
        var storage = Assert.IsType<LegacyStorage>(brain.SiloServices.GetRequiredKeyedService<IGrainStorage>(DigitalBrainNames.DefaultGrainStorage));
        Assert.True(storage.Reads >= 4);
    }

    private sealed class LegacyStorage(IGrainStorageSerializer serializer, bool alreadyCompleted = false, bool unfinishedGrantOnly = false) : IGrainStorage
    {
        private readonly Dictionary<(string, GrainId), BinaryData> _written = [];
        public int Reads { get; private set; }
        public int Writes { get; private set; }

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
            if (alreadyCompleted && state.State is IdentityDirectoryState directory)
            { state.State = (T)(object)(directory with { MigrationComplete = true }); }
            if (unfinishedGrantOnly && state.State is IdentityDirectoryState)
            { state.State = (T)(object)new IdentityDirectoryState { MigrationPlanId = "unfinished-grant-only-plan" }; }
            state.RecordExists = true;
            state.ETag = "legacy";
        }

        public Task WriteStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> state)
        {
            Writes++;
            _written[(stateName, grainId)] = serializer.Serialize(state.State);
            state.RecordExists = true;
            state.ETag = "updated";
            return Task.CompletedTask;
        }

        public Task ClearStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> state)
            => throw new NotSupportedException();
    }
}
