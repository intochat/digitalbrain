using Brain.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Brain.Client;

public sealed class BrainCluster : IAsyncDisposable
{
    private readonly IHost _host;

    private BrainCluster(IHost host, IClusterClient client) => (_host, Client) = (host, client);

    public IClusterClient Client { get; }

    public static async Task<BrainCluster> Connect(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.AddBrainClient();
        var host = builder.Build();
        await host.StartAsync();
        return new BrainCluster(host, host.Services.GetRequiredService<IClusterClient>());
    }

    public T Get<T>(string addressKey) where T : class, INeuronContract =>
        NeuronProxy.Create<T>(Client, addressKey, "local-owner|actor/script|session/" + Guid.NewGuid().ToString("N")[..8]);

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }
}

public static class BrainClientExtensions
{
    public static TBuilder AddBrainClient<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var clusterId = builder.Configuration["Orleans:ClusterId"] ?? "dev";
        var serviceId = builder.Configuration["Orleans:ServiceId"] ?? "dev";
        builder.UseOrleansClient(client => client.UseLocalhostClustering(clusterId: clusterId, serviceId: serviceId));
        return builder;
    }
}
