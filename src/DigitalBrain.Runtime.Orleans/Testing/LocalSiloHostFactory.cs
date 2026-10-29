using System.Net;
using System.Net.Sockets;
using DigitalBrain.Spi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using OrleansSdk = Orleans;
namespace DigitalBrain.Runtime.Orleans.Testing;

/// <summary>
/// Backs <c>SiloHost.Local()</c> from the test kit (found by type name, no reference): an in-process silo over the local topology on a Home folder.
/// Without a cluster store registration the <c>cluster</c> store stays <see cref="UnregisteredClusterStorage"/>.
/// </summary>
public sealed class LocalSiloHostFactory : IInProcessHostFactory
{
    private readonly IClusterStoreRegistration _clusterStore;
    private readonly Action<IServiceCollection>? _configureServices;

    /// <summary>A factory whose hosts leave the <c>cluster</c> store unregistered.</summary>
    public LocalSiloHostFactory() : this(null) { }

    /// <summary>A factory whose hosts use <paramref name="clusterStore"/> for the <c>cluster</c> store.</summary>
    public LocalSiloHostFactory(IClusterStoreRegistration? clusterStore) : this(clusterStore, null) { }

    /// <summary>
    /// A factory whose hosts use <paramref name="clusterStore"/> and then run <paramref name="configureServices"/> on the silo
    /// services after every topology and runtime registration, so a fact can replace services such as <see cref="TimeProvider"/>.
    /// </summary>
    public LocalSiloHostFactory(IClusterStoreRegistration? clusterStore, Action<IServiceCollection>? configureServices)
    {
        _clusterStore = clusterStore ?? new Unregistered();
        _configureServices = configureServices;
    }

    /// <inheritdoc />
    public async Task<IInProcessHost> Start(InProcessHostOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);
        var temporary = options.Home is null;
        var home = options.Home ?? Path.Combine(Path.GetTempPath(), "digitalbrain-" + Guid.NewGuid().ToString("N"));
        var host = new LocalSiloHost(this, options, Path.GetFullPath(home), temporary);
        try { await host.Start(ct).ConfigureAwait(false); }
        catch
        {
            await host.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        return host;
    }

    internal IHost Build(InProcessHostOptions options, string home)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [IntoChatOSNames.TopologyKey] = Topology.Local.ToConfigValue(),
            [IntoChatOSNames.HomeKey] = home,
            [TopologyContext.SiloPortKey] = FreePort().ToString(System.Globalization.CultureInfo.InvariantCulture),
            [TopologyContext.GatewayPortKey] = FreePort().ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        foreach (var (key, value) in options.Configuration) { values[key] = value; }
        builder.Configuration.AddInMemoryCollection(values);
        builder.UseClusterStore(_clusterStore);
        builder.UseModuleTopology(options.Modules, []);
        builder.UseOrleans(silo =>
        {
            silo.ConfigureTopologySilo();
            _configureServices?.Invoke(silo.Services);
        });
        return builder.Build();
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class Unregistered : IClusterStoreRegistration
    {
        // LocalProfile already registered UnregisteredClusterStorage; nothing replaces it.
        public void Register(OrleansSdk.Hosting.ISiloBuilder silo) { }
    }
}

internal sealed class LocalSiloHost(LocalSiloHostFactory factory, InProcessHostOptions options, string home, bool temporary) : IInProcessHost
{
    private IHost? _host;

    public IServiceProvider Services => (_host ?? throw new ObjectDisposedException(nameof(LocalSiloHost))).Services;

    public string Home => home;

    internal async Task Start(CancellationToken ct)
    {
        _host = factory.Build(options, home);
        await _host.StartAsync(ct).ConfigureAwait(false);
    }

    public async Task Restart(CancellationToken ct)
    {
        await Stop(ct).ConfigureAwait(false);
        await Start(ct).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await Stop(CancellationToken.None).ConfigureAwait(false);
        if (temporary && Directory.Exists(home))
        {
            for (var attempt = 0; ; attempt++)
            {
                try { Directory.Delete(home, recursive: true); break; }
                catch (IOException) when (attempt < 10) { await Task.Delay(100).ConfigureAwait(false); }
                catch (UnauthorizedAccessException) when (attempt < 10) { await Task.Delay(100).ConfigureAwait(false); }
            }
        }
    }

    private async Task Stop(CancellationToken ct)
    {
        if (_host is null) { return; }
        var host = _host;
        _host = null;
        try { await host.StopAsync(ct).ConfigureAwait(false); }
        finally
        {
            if (host is IAsyncDisposable async) { await async.DisposeAsync().ConfigureAwait(false); }
            else { host.Dispose(); }
        }
    }
}
