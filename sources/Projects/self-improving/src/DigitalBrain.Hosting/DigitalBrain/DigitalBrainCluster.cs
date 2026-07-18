using DigitalBrain.Protocol;
using DigitalBrain.Os.Application;
using static DigitalBrain.Os.Application.Brain;

namespace DigitalBrain.Hosting.DigitalBrain;

public sealed class DigitalBrainCluster : IAsyncDisposable
{
    public IClusterClient Client { get; }
    public IDigitalBrain Brain { get; }
    public IMarketplace Marketplace { get; }
    public IPackager Packager { get; }

    public DigitalBrainCluster(IClusterClient client)
    {
        Client = client;
        Brain = client.GetGrain<IDigitalBrain>(WellKnownKey);
        Marketplace = client.GetGrain<IMarketplace>(WellKnownKey);
        Packager = client.GetGrain<IPackager>(WellKnownKey);
    }

    public T Get<T>(string key) where T : INeuron => Client.GetGrain<T>(key);

    public ValueTask DisposeAsync()
    {
        if (Client is IAsyncDisposable ad) return ad.DisposeAsync();
        if (Client is IDisposable d) d.Dispose();
        return ValueTask.CompletedTask;
    }
}