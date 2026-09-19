using System.Runtime.CompilerServices;
using System.Threading.Channels;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.TestingHost;

namespace DigitalBrain.Testing;

public static class DigitalBrainSimulation
{
    public static Task<IDigitalBrain> StartAsync() => StartAsync(new());

    public static async Task<IDigitalBrain> StartAsync(DigitalBrainSimulationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var builder = new InProcessTestClusterBuilder(1);
        builder.ConfigureHost(static host => host.Logging.SetMinimumLevel(LogLevel.Warning));
        if (options.Configuration is { Count: > 0 } configuration)
        {
            builder.ConfigureHost(host => host.Configuration.AddInMemoryCollection(configuration));
        }

        builder.ConfigureSilo((_, silo) =>
        {
            silo.AddNeuronBroadcast();
            foreach (var module in options.Modules)
            {
                module.Configure(silo);
            }

            options.ConfigureSilo?.Invoke(silo);
        });
        builder.ConfigureClient(static client => client.AddNeuronBroadcast());
        var cluster = builder.Build();
        await cluster.DeployAsync().ConfigureAwait(false);

        var web = WebApplication.CreateBuilder();
        web.WebHost.UseUrls("http://127.0.0.1:0");
        web.Logging.ClearProviders();
        web.Services.AddSingleton<IGrainFactory>(cluster.Client);
        web.Services.AddSingleton<IClusterClient>(cluster.Client);
        var app = web.Build();
        foreach (var module in options.Modules)
        {
            module.Configure(app);
        }

        await app.StartAsync().ConfigureAwait(false);
        var brain = new HostedDigitalBrain(cluster, app);
        await brain.Signals().ConfigureAwait(false);
        return brain;
    }
}

public sealed class DigitalBrainSimulationOptions
{
    public Action<ISiloBuilder>? ConfigureSilo { get; init; }
    public IReadOnlyDictionary<string, string?>? Configuration { get; init; }
    public IReadOnlyList<IModule> Modules { get; init; } = [];
}

file sealed class HostedDigitalBrain(InProcessTestCluster cluster, WebApplication app) : IDigitalBrain
{
    public HttpClient Http { get; } = new() { BaseAddress = new Uri(app.Urls.Single()) };

    public T Get<T>(string id) where T : class, IGrainWithStringKey
        => cluster.Client.GetGrain<T>(id);

    public Task<IReadOnlyList<Signal>> Signals()
        => cluster.Client.GetGrain<IDigitalBrainLive>(NeuronBroadcast.Everyone).Signals();

    public async IAsyncEnumerable<T> On<T>(INeuron neuron, [EnumeratorCancellation] CancellationToken cancellation = default)
        where T : Signal
    {
        ArgumentNullException.ThrowIfNull(neuron);
        var messages = Channel.CreateUnbounded<T>();
        var catcher = new SignalCatcher<T>(messages.Writer);
        var reference = cluster.Client.CreateObjectReference<INeuronObserver>(catcher);
        await neuron.Watch(reference);
        try
        {
            await foreach (var signal in messages.Reader.ReadAllAsync(cancellation))
            {
                yield return signal;
            }
        }
        finally
        {
            await neuron.Unwatch(reference);
            cluster.Client.DeleteObjectReference<INeuronObserver>(reference);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Http.Dispose();
        await app.DisposeAsync();
        await cluster.DisposeAsync();
    }

    private sealed class SignalCatcher<T>(ChannelWriter<T> writer) : INeuronObserver
        where T : Signal
    {
        public Task Hear(Signal signal)
        {
            if (signal is T typed)
            {
                writer.TryWrite(typed);
            }

            return Task.CompletedTask;
        }
    }
}
