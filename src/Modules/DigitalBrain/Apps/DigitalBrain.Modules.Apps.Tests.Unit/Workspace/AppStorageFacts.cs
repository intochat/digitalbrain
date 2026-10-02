using DigitalBrain.Apps;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
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
    }
}
