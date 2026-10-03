using DigitalBrain.Apps;
using DigitalBrain;
using DigitalBrain.Contracts;
using DigitalBrain.Kernel;
using DigitalBrain.Postgres;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Storage;

namespace DigitalBrain.Modules.Apps.Tests.Unit.Workspace;

public sealed class AppStorageFacts
{
    [Fact]
    public async Task RemovingPostgresCannotTurnAnUninstallIntoFalseSuccess()
    {
        var ct = TestContext.Current.CancellationToken;
        var storage = new FlakyGrainStorage();
        const string key = "requires-postgres";
        await using (var brain = await UnitTest.Create().WithModule<AppsModule>().WithModule<PostgresModule>()
            .ConfigureSilo(silo =>
            {
                silo.Configuration["ConnectionStrings:postgres"] = "Host=localhost;Database=sample;Username=reader";
                silo.Services.AddKeyedSingleton<IGrainStorage>(DigitalBrainNames.DefaultGrainStorage, storage);
            }).StartAsync(ct))
        {
            Caller.As("alice");
            var package = PackageId.Parse("alice/storage");
            var content = PackageSamples.Researcher("Research") with
            { Source = "#:project /brain/DigitalBrain.Modules.Postgres.Contracts.csproj\n// table behavior" };
            var revision = await brain.Get<IPackage>(package.ToString()).Commit(new(Guid.NewGuid(), null, content, "Storage"));
            await brain.Get<IApp>(key).Install(new(Guid.NewGuid(), new(package, revision.Id), new Dictionary<string, string>()));
        }
        await using var restarted = await PackageBrain.StartAsync(ct, silo =>
            silo.Services.AddKeyedSingleton<IGrainStorage>(DigitalBrainNames.DefaultGrainStorage, storage));
        Caller.As("alice");
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
