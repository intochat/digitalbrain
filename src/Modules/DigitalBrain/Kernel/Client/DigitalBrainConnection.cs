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
    private readonly GatewayRelay? _relay;

    internal DigitalBrainConnection(IHost host, IDigitalBrain brain, IConfiguration configuration, Activity? activity, GatewayRelay? relay)
    {
        _host = host;
        _brain = brain;
        _activity = activity;
        _relay = relay;
        Configuration = configuration;
    }

    public IConfiguration Configuration { get; }

    public CancellationToken Stopping => _host.Services.GetService(typeof(IHostApplicationLifetime)) is IHostApplicationLifetime lifetime
        ? lifetime.ApplicationStopping : CancellationToken.None;

    // Environment variables spell configuration sections with "__", so "Account__twitter" arrives as "Account:twitter".
    public string? Setting(string name) => Configuration[SettingKey(name)];

    internal static string SettingKey(string name) => "CSharpFile:Settings:" + name.Replace("__", ":", StringComparison.Ordinal);

    public T Get<T>(string id) where T : class, IGrainWithStringKey => _brain.Get<T>(id);

    public Task<ISignalSubscription<T>> SubscribeAsync<T>(INeuron source, CancellationToken cancellationToken = default) where T : Signal
        => _brain.SubscribeAsync<T>(source, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await _brain.DisposeAsync().ConfigureAwait(false);
        using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await _host.StopAsync(shutdown.Token).ConfigureAwait(false);
        _host.Dispose();
        if (_relay is not null) { await _relay.DisposeAsync().ConfigureAwait(false); }
        _activity?.Dispose();
    }
}
