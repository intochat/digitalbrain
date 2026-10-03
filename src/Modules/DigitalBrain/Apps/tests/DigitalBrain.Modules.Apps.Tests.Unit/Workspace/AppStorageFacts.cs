using DigitalBrain;
using DigitalBrain.Apps;
using DigitalBrain.Contracts;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Enforcement;
using DigitalBrain.Postgres;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Storage;

namespace DigitalBrain.Modules.Apps.Tests.Unit.Workspace;

public sealed class AppStorageFacts
{
    [Fact]
    public async Task ConfigureAndUpgradeMigrateLegacyRowsThenUninstallRetiresTheStableOwner()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new SavedTable();
        await using var brain = await UnitTest.Create().WithModule<AppsModule>().WithModule<PostgresModule>()
            .ConfigureSilo(silo =>
            {
                silo.Configuration["ConnectionStrings:postgres"] = "Host=localhost;Database=sample;Username=reader";
                silo.Services.AddSingleton<IPostgresTableProvider>(provider);
            }).StartAsync(ct);
        Caller.As("alice");
        await brain.AuthorizeCallerAsync();
        var package = PackageId.Parse("alice/storage-lifetime");
        var content = PackageSamples.Researcher("Research");
        var revision = await brain.Get<IPackage>(package.ToString()).Commit(new(Guid.NewGuid(), null, content, "First"));
        var app = brain.Get<IApp>(BrainScope.CurrentId() + "/apps/stable-storage-install");
        var installed = await app.Install(new(Guid.NewGuid(), new(package, revision.Id), new Dictionary<string, string>()));
        static void AsApp(string id) => CallerContextStamper.Stamp(CallerContextStamper.Require() with { Kind = CallerKind.App, StampedBy = TrustedEdge.AppProxy, AppId = id });
        AsApp(installed.CSharpFiles.Single());
        var table = brain.Get<IPostgresTable>("storage-lifetime-rows");
        var physical = await table.Define(new([new("id", "text"), new("value", "text")], ["id"]));
        TableValue[] key = [new("id", "\"saved\"")];
        await table.Upsert(key, [new("value", "\"original row\"")]);
        Caller.As("alice");
        await brain.AuthorizeCallerAsync();
        await app.Configure(new(Guid.NewGuid(), new Dictionary<string, string>()));
        AsApp(BrainScope.CurrentId() + "/apps/stable-storage-install");
        Assert.Equal("\"original row\"", (await table.Read(key))!.Single(v => v.Column == "value").Json);
        Caller.As("alice");
        await brain.AuthorizeCallerAsync();
        var next = await brain.Get<IPackage>(package.ToString()).Commit(new(Guid.NewGuid(), revision.Id, content with { Source = "// upgraded" }, "Upgrade"));
        await app.Upgrade(new(Guid.NewGuid(), new(package, next.Id)));
        AsApp(BrainScope.CurrentId() + "/apps/stable-storage-install");
        Assert.Equal("\"original row\"", (await table.Read(key))!.Single(v => v.Column == "value").Json);
        Caller.As("alice");
        await brain.AuthorizeCallerAsync();
        await app.Uninstall(new(Guid.NewGuid()));
        Assert.Equal(physical.Table, Assert.Single(provider.Drops));
        Assert.Empty(provider.Row);
    }

    private sealed class SavedTable : IPostgresTableProvider
    {
        public TableValue[] Row { get; private set; } = [];
        public List<string> Drops { get; } = [];
        public Task DefineAsync(string origin, string table, TableDefinition definition, CancellationToken ct) => Task.CompletedTask;
        public Task DropAsync(string origin, string table, CancellationToken ct) { Drops.Add(table); Row = []; return Task.CompletedTask; }
        public Task<bool> UpsertAsync(string origin, string table, TableDefinition definition, TableValue[] key, TableValue[] values, CancellationToken ct)
        { Row = [.. key, .. values]; return Task.FromResult(true); }
        public Task<bool> DeleteAsync(string origin, string table, TableDefinition definition, TableValue[] key, CancellationToken ct)
        { Row = []; return Task.FromResult(true); }
        public Task<TableValue[]?> ReadAsync(string origin, string table, TableDefinition definition, TableValue[] key, CancellationToken ct) => Task.FromResult<TableValue[]?>(Row);
        public Task<TableValue[][]> PageAsync(string origin, string table, TableDefinition definition, int offset, int limit, CancellationToken ct) => Task.FromResult<TableValue[][]>([Row]);
    }

    [Fact]
    public async Task RemovingPostgresCannotTurnAnUninstallIntoFalseSuccess()
    {
        var ct = TestContext.Current.CancellationToken;
        var storage = new FlakyGrainStorage();
        var key = BrainScope.Create("account-alice", "workspace-alice").Id + "/apps/requires-postgres";
        await using (var brain = await UnitTest.Create().WithModule<AppsModule>().WithModule<PostgresModule>()
            .ConfigureSilo(silo =>
            {
                silo.Configuration["ConnectionStrings:postgres"] = "Host=localhost;Database=sample;Username=reader";
                silo.Services.AddKeyedSingleton<IGrainStorage>(DigitalBrainNames.DefaultGrainStorage, storage);
            }).StartAsync(ct))
        {
            Caller.As("alice");
            await brain.AuthorizeCallerAsync();
            var package = PackageId.Parse("alice/storage");
            var content = PackageSamples.Researcher("Research") with
            { Source = "#:project /brain/DigitalBrain.Modules.Postgres.Contracts.csproj\n// table behavior" };
            var revision = await brain.Get<IPackage>(package.ToString()).Commit(new(Guid.NewGuid(), null, content, "Storage"));
            await brain.Get<IApp>(key).Install(new(Guid.NewGuid(), new(package, revision.Id), new Dictionary<string, string>()));
        }
        await using var restarted = await PackageBrain.StartAsync(ct, silo =>
            silo.Services.AddKeyedSingleton<IGrainStorage>(DigitalBrainNames.DefaultGrainStorage, storage));
        Caller.As("alice");
        await restarted.AuthorizeCallerAsync();
        var app = restarted.Get<IApp>(key);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => app.Uninstall(new(Guid.NewGuid())));
        Assert.Equal("Cannot uninstall this app. Restore the Postgres module to remove its storage.", error.Message);
        Assert.Equal(AppStatus.Installed, (await app.Read()).Status);
        Assert.True((await app.Read()).UninstallPending);
        var originalFiles = (await app.Read()).CSharpFiles.ToArray();
        var abandon = new AbandonAppStorage(Guid.NewGuid());
        storage.FailNextWrite = state => state is AppState { Status: AppStatus.Uninstalled };
        await Assert.ThrowsAnyAsync<Exception>(() => app.AbandonStorage(abandon));
        await restarted.Brain.DeactivateAsync(app, ct);
        var uninstalled = await app.AbandonStorage(abandon);
        Assert.Equal(AppStatus.Uninstalled, uninstalled.Status);
        Assert.False(uninstalled.UninstallPending);
        var leftBehind = Assert.Single(uninstalled.AbandonedStorage!);
        Assert.Equal(abandon.OperationId, leftBehind.OperationId);
        Assert.Equal(originalFiles, leftBehind.Files);

        var replacement = PackageId.Parse("alice/replacement");
        var next = await restarted.Get<IPackage>(replacement.ToString()).Commit(restarted.Commit(null, PackageSamples.Researcher("Research")));
        var installed = await app.Install(new(Guid.NewGuid(), new(replacement, next.Id), new Dictionary<string, string>()));
        await restarted.Brain.DeactivateAsync(app, ct);
        var replayed = await app.AbandonStorage(abandon);
        Assert.Equal(AppStatus.Installed, replayed.Status);
        Assert.Equal(installed.CSharpFiles, replayed.CSharpFiles);
        Assert.Single(replayed.AbandonedStorage!);
    }

    [Fact]
    public async Task StorageCannotBeAbandonedWithoutAPendingUninstall()
    {
        await using var brain = await PackageBrain.StartAsync(TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidOperationException>(() => brain.Get<IApp>("no-uninstall").AbandonStorage(new(Guid.NewGuid())));
    }
}
