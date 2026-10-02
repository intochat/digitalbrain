using DigitalBrain.Apps;
using DigitalBrain.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Storage;

namespace DigitalBrain.Modules.Apps.Tests.Unit;

public sealed class LegacyAppStateFacts
{
    [Fact]
    public async Task PreLifecycleAppStateReactivatesAndCanInstallAndUninstallAgain()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await PackageBrain.StartAsync(ct, silo =>
            silo.Services.AddKeyedSingleton<IGrainStorage>(DigitalBrainNames.DefaultGrainStorage, (services, _) =>
                new LegacyStorage(new OrleansGrainStorageSerializer(services.GetRequiredService<Serializer>()))));
        var app = brain.Get<IApp>("legacy-app");
        Assert.Equal(AppStatus.Uninstalled, (await app.Read()).Status);
        Caller.As("alice");
        var package = PackageId.Parse("alice/legacy");
        var revision = await brain.Get<IPackage>(package.ToString()).Commit(brain.Commit(null, PackageSamples.Researcher("Research")));
        Caller.Clear();
        var request = new InstallApp(Guid.NewGuid(), new(package, revision.Id), new Dictionary<string, string>());
        var installed = await app.Install(request);
        await brain.Brain.DeactivateAsync(app, ct);
        Assert.Equal(installed.CSharpFiles, (await app.Install(request)).CSharpFiles);
        Assert.Equal(AppStatus.Uninstalled, (await app.Uninstall(new(Guid.NewGuid()))).Status);
        await brain.Brain.DeactivateAsync(app, ct);
        Assert.Equal(AppStatus.Uninstalled, (await app.Uninstall(new(Guid.NewGuid()))).Status);
    }

    private sealed class LegacyStorage(IGrainStorageSerializer serializer) : IGrainStorage
    {
        private readonly Dictionary<(string, GrainId), BinaryData> written = [];
        public async Task ReadStateAsync<T>(string name, GrainId id, IGrainState<T> state)
        {
            if (!written.TryGetValue((name, id), out var bytes))
            {
                if (typeof(T) != typeof(AppState)) { return; }
                bytes = new(await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "LegacyState", "app.orleans"), TestContext.Current.CancellationToken));
            }
            state.State = serializer.Deserialize<T>(bytes);
            state.RecordExists = true;
            state.ETag = "read";
        }
        public Task WriteStateAsync<T>(string name, GrainId id, IGrainState<T> state)
        {
            written[(name, id)] = serializer.Serialize(state.State);
            state.RecordExists = true;
            state.ETag = "written";
            return Task.CompletedTask;
        }
        public Task ClearStateAsync<T>(string name, GrainId id, IGrainState<T> state) => throw new NotSupportedException();
    }
}
