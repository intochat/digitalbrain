using DigitalBrain.Apps;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.Storage;

namespace DigitalBrain.Modules.Apps.Tests.Unit;

internal sealed class PackageBrain(ModuleBrain brain, FlakyGrainStorage storage) : IAsyncDisposable
{
    public FlakyGrainStorage Storage => storage;

    public ModuleBrain Brain => brain;
    public Task AuthorizeCallerAsync() => brain.AuthorizeCallerAsync();

    public T Get<T>(string key) where T : class, IGrainWithStringKey => brain.Get<T>(key);

    public CommitPackage Commit(string? expectedHead, PackageContent content, string message = "Change", PackageRevisionRef? mergeFrom = null)
        => new(Guid.NewGuid(), expectedHead, content, message, mergeFrom);

    public static async Task<PackageBrain> StartAsync(CancellationToken cancellationToken, Action<ISiloBuilder>? configureSilo = null, Action<IClientBuilder>? configureClient = null)
    {
        var storage = new FlakyGrainStorage();
        var brain = await ModuleTest.Create()
            .WithModule<AppsModule>()
            .ConfigureClient(client => configureClient?.Invoke(client))
            .ConfigureSilo(silo =>
            {
                silo.Services.AddKeyedSingleton<IGrainStorage>("Default", storage);
                configureSilo?.Invoke(silo);
            })
            .StartAsync(cancellationToken);
        return new(brain, storage);
    }

    public ValueTask DisposeAsync()
    {
        Caller.Clear();
        return brain.DisposeAsync();
    }
}
