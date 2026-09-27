using System.Runtime.CompilerServices;
using System.Text.Json;
using DigitalBrain.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DigitalBrain.Client;

public sealed class DigitalBrainConnection : IDigitalBrain
{
    private readonly IHost _host;
    private readonly GatewayRelay? _relay;
    private readonly IDigitalBrain _brain;

    internal DigitalBrainConnection(IHost host, GatewayRelay? relay)
    {
        _host = host;
        _relay = relay;
        _brain = host.Services.GetRequiredService<IDigitalBrain>();
    }

    public CancellationToken Stopping => _host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping;

    // Environment variables spell configuration sections with "__", so "Account__twitter" arrives as "Account:twitter".
    public string? Setting(string name) => _host.Services.GetRequiredService<IConfiguration>()[SettingKey(name)];

    // A run started by an armed file's trigger carries that signal; other runs have none.
    public T Trigger<T>() where T : Signal
        => _host.Services.GetRequiredService<IConfiguration>()["CSharpFile:Trigger"] is { Length: > 0 } json
            ? JsonSerializer.Deserialize<T>(json, JsonSerializerOptions.Web) ?? throw new InvalidOperationException("The trigger signal is empty.")
            : throw new InvalidOperationException("This run was not started by a trigger.");

    private static string SettingKey(string name) => "CSharpFile:Settings:" + name.Replace("__", ":", StringComparison.Ordinal);

    public T Get<T>(string id) where T : class, IGrainWithStringKey => _brain.Get<T>(id);

    public Task<ISignalSubscription<T>> SubscribeAsync<T>(INeuron source, CancellationToken cancellationToken = default) where T : Signal
        => _brain.SubscribeAsync<T>(source, cancellationToken);

    public async IAsyncEnumerable<T> On<T>(INeuron source, [EnumeratorCancellation] CancellationToken cancellationToken = default) where T : Signal
    {
        await using var subscription = await SubscribeAsync<T>(source, cancellationToken).ConfigureAwait(false);
        await foreach (var signal in subscription.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return signal;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _brain.DisposeAsync().ConfigureAwait(false);
        using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await _host.StopAsync(shutdown.Token).ConfigureAwait(false);
        _host.Dispose();
        if (_relay is not null) { await _relay.DisposeAsync().ConfigureAwait(false); }
    }
}
