using System.Diagnostics;
using DigitalBrain.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace DigitalBrain.Core;

public sealed class DigitalBrainConnection : IDigitalBrain
{
    private readonly IHost _host;
    private readonly IDigitalBrain _brain;
    private readonly Activity? _activity;

    internal DigitalBrainConnection(IHost host, IDigitalBrain brain, IConfiguration configuration, Activity? activity)
    {
        _host = host;
        _brain = brain;
        _activity = activity;
        Configuration = configuration;
    }

    public IConfiguration Configuration { get; }

    public CancellationToken Stopping => _host.Services.GetService(typeof(IHostApplicationLifetime)) is IHostApplicationLifetime lifetime
        ? lifetime.ApplicationStopping : CancellationToken.None;

    public string? Setting(string name) => Configuration[$"CSharpFile:Settings:{name}"];

    public T Get<T>(string id) where T : class, IGrainWithStringKey => _brain.Get<T>(id);

    public Task<ISignalSubscription<T>> SubscribeAsync<T>(INeuron source, CancellationToken cancellationToken = default) where T : Signal
        => _brain.SubscribeAsync<T>(source, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await _brain.DisposeAsync().ConfigureAwait(false);
        using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await _host.StopAsync(shutdown.Token).ConfigureAwait(false);
        _host.Dispose();
        _activity?.Dispose();
    }
}
