using Brain.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Brain.Client;

public sealed class BrainCluster : IAsyncDisposable
{
    private readonly IHost _host;

    private BrainCluster(IHost host, IClusterClient client, string callerKey) =>
        (_host, Client, CallerKey) = (host, client, callerKey);

    public IClusterClient Client { get; }

    public string CallerKey { get; }

    public static Task<BrainCluster> Connect(string[] args) => ConnectAs(args, forcedCallerKey: null);

    public static Task<BrainCluster> Connect(string[] args, string callerKey) => ConnectAs(args, callerKey);

    public static string ResolveCallerKey(string? forcedCallerKey, IConfiguration configuration) =>
        forcedCallerKey
            ?? configuration["BRAIN_CALLER"]
            ?? "local-owner|actor/script|session/" + Guid.NewGuid().ToString("N")[..8];

    private static async Task<BrainCluster> ConnectAs(string[] args, string? forcedCallerKey)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.AddBrainClient();
        var host = builder.Build();
        await host.StartAsync();
        var callerKey = ResolveCallerKey(forcedCallerKey, builder.Configuration);
        return new BrainCluster(host, host.Services.GetRequiredService<IClusterClient>(), callerKey);
    }

    public T Get<T>(string addressKey) where T : class, INeuronContract =>
        NeuronProxy.Create<T>(Client, addressKey, CallerKey);

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
