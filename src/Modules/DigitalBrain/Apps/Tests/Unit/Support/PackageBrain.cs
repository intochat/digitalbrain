using DigitalBrain.Apps;
using DigitalBrain.Coding;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;

namespace DigitalBrain.Modules.Apps.Tests.Unit;

internal sealed class PackageBrain(UnitBrain brain, FakeArtifactStore artifacts) : IAsyncDisposable
{
    public FakeArtifactStore Artifacts => artifacts;

    public UnitBrain Brain => brain;

    public T Get<T>(string key) where T : class, IGrainWithStringKey => brain.Get<T>(key);

    public CommitPackage Commit(string? expectedHead, PackageContent content, string message = "Change", PackageRevisionRef? mergeFrom = null)
        => new(Guid.NewGuid(), expectedHead, content, artifacts.Seal(content), message, mergeFrom);

    public static async Task<PackageBrain> StartAsync(CancellationToken cancellationToken, Action<ISiloBuilder>? configureSilo = null)
    {
        var artifacts = new FakeArtifactStore();
        var brain = await UnitTest.Create()
            .WithModule<AppsModule>()
            .ConfigureSilo(silo =>
            {
                silo.Services.AddSingleton<ICodeArtifactStore>(artifacts);
                configureSilo?.Invoke(silo);
            })
            .StartAsync(cancellationToken);
        return new(brain, artifacts);
    }

    public ValueTask DisposeAsync()
    {
        Caller.Clear();
        return brain.DisposeAsync();
    }
}
