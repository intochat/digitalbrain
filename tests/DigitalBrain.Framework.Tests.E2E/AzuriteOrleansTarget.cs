using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Azure.Storage.Blobs;
using DigitalBrain.Runtime.Orleans;
using DigitalBrain.Runtime.Orleans.Azure;
using DigitalBrain.Runtime.Orleans.Azure.Tests.Integration;
using DigitalBrain.Spi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DigitalBrain.Framework.Tests.E2E;

/// <summary>
/// The cloud topology (CloudProfile: Azure queue streams, table PubSubStore, table clustering) against Azurite. No Key Vault is
/// configured (04-06 R4-08): the profile falls back to the machine-key secrets source.
/// </summary>
public sealed class AzuriteOrleansTarget(AzuriteFixture azurite) : OrleansFrameworkTarget
{
    public const string TargetName = "azurite-orleans";

    public override string Name => TargetName;

    public override async Task<IInProcessHost> Start(CancellationToken ct) => await Started(new CloudHost(azurite.Require(), null, null, null), ct);

    public override async Task<RunningTarget> StartHost(FrameworkHostOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);
        var clock = new OffsetClock();
        var host = await Started(new CloudHost(azurite.Require(), [typeof(FrameworkModule)], OrleansRun.Configuration(options), OrleansRun.ClockServices(clock)), ct);
        return OrleansRun.Started(host, options, clock);
    }

    private static async Task<IInProcessHost> Started(CloudHost host, CancellationToken ct)
    {
        try { await host.Start(ct); }
        catch
        {
            await host.DisposeAsync();
            throw;
        }
        return host;
    }

    private sealed class CloudHost(
        string storage,
        IReadOnlyList<Type>? modules,
        IReadOnlyDictionary<string, string?>? configuration,
        Action<IServiceCollection>? configureServices) : IInProcessHost
    {
        private const string MasterKey = "e2e-master-key-from-the-in-memory-vault";
        private readonly string _clusterId = "e2e" + Guid.NewGuid().ToString("N")[..12];
        private IHost? _host;

        public IServiceProvider Services => (_host ?? throw new ObjectDisposedException(nameof(CloudHost))).Services;

        public string Home { get; } = Path.Combine(Path.GetTempPath(), "db-e2e-" + Guid.NewGuid().ToString("N"));

        public async Task Start(CancellationToken ct)
        {
            // The environment provisions the brain lease container (BlobLeaseBrainLogOwnership); on Azurite the target does.
            await new BlobServiceClient(storage).GetBlobContainerClient(CloudProfile.LeaseContainer).CreateIfNotExistsAsync(cancellationToken: ct);
            _host = Build();
            await _host.StartAsync(ct);
        }

        public async Task Restart(CancellationToken ct)
        {
            await Stop(ct);
            await Start(ct);
        }

        public async ValueTask DisposeAsync()
        {
            await Stop(CancellationToken.None);
            for (var attempt = 0; Directory.Exists(Home); attempt++)
            {
                try { Directory.Delete(Home, recursive: true); }
                catch (IOException) when (attempt < 10) { await Task.Delay(100); }
                catch (UnauthorizedAccessException) when (attempt < 10) { await Task.Delay(100); }
            }
        }

        private async Task Stop(CancellationToken ct)
        {
            if (_host is null) { return; }
            var host = _host;
            _host = null;
            try { await host.StopAsync(ct); }
            finally { host.Dispose(); }
        }

        private IHost Build()
        {
            var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
            var values = new Dictionary<string, string?>
            {
                [IntoChatOSNames.TopologyKey] = "cloud",
                [IntoChatOSNames.HomeKey] = Home,
                [IntoChatOSNames.CloudStorageKey] = storage,
                [TopologyContext.SiloPortKey] = FreePort().ToString(CultureInfo.InvariantCulture),
                [TopologyContext.GatewayPortKey] = FreePort().ToString(CultureInfo.InvariantCulture),
            };
            // The 03-09 probe host keeps its vault URL with the in-memory master key; framework hosts run with no vault at all.
            if (modules is null) { values[IntoChatOSNames.CloudKeyVaultKey] = "https://e2e-vault.vault.azure.net/"; }
            foreach (var (key, value) in configuration ?? new Dictionary<string, string?>()) { values[key] = value; }
            builder.Configuration.AddInMemoryCollection(values);
            if (modules is null)
            {
                builder.UseClusterStore(_ => { });
                var cloud = new CloudProfile((config, _) => config.AddInMemoryCollection(
                    new Dictionary<string, string?> { [IntoChatOSNames.MasterKeyKey] = MasterKey }));
                builder.UseTopology([], [], cloud);
            }
            else
            {
                builder.UseClusterStore(new BrainStorageRegistration([.. modules.Select(module => module.Assembly).Distinct()]));
                builder.UseModuleTopology(modules, [], new CloudProfile());
            }
            builder.UseOrleans(silo =>
            {
                silo.ConfigureTopologySilo();
                silo.Configure<Orleans.Configuration.ClusterOptions>(o => { o.ClusterId = _clusterId; o.ServiceId = _clusterId; });
                silo.Configure<Orleans.Configuration.EndpointOptions>(o => o.AdvertisedIPAddress = IPAddress.Loopback);
                configureServices?.Invoke(silo.Services);
            });
            return builder.Build();
        }

        private static int FreePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
    }
}
