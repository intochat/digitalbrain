using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Hosting;
using Orleans.Storage;

namespace DigitalBrain.Runtime.Orleans;

/// <summary>
/// Everything on this machine (P2-D-09, P2-D-12 revised): localhost clustering on loopback, file grain stores for
/// <c>platform</c>, <c>PubSubStore</c> and every module grain store under <c>&lt;Home&gt;/state/files/&lt;name&gt;</c>, a file
/// reminder table, memory streams for <c>signals</c> over the file <c>PubSubStore</c>, and the machine-key master key.
/// The <c>cluster</c> store is host-bound (BrainLog); until the host registers it, it is <see cref="UnregisteredClusterStorage"/>.
/// </summary>
public sealed class LocalProfile : ITopologyProfile, IRuntimeOptionsHolder
{
    /// <summary>The os HTTP port (os Kernel launch settings).</summary>
    public const int HttpPort = 5310;

    /// <summary>The os container image.</summary>
    public const string Image = RuntimeOptions.IntoChatOsImage;

    /// <summary>The sandbox container image.</summary>
    public const string SandboxImage = RuntimeOptions.IntoChatSandboxImage;

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<RuntimeOptions, Services> ServicesByOptions = new();

    private readonly RuntimeOptions _options;
    private readonly HashSet<string> _reserved;

    /// <summary>Creates the local topology.</summary>
    /// <param name="options">The runtime's key and store names; null means <see cref="RuntimeOptions.IntoChatCompatible"/>.</param>
    public LocalProfile(RuntimeOptions? options = null)
    {
        _options = options ?? RuntimeOptions.IntoChatCompatible;
        _reserved = new(StringComparer.OrdinalIgnoreCase) { _options.PlatformGrainStorage, _options.PubSubStore };
        Services = ServicesFor(_options);
        Defaults = new Dictionary<string, string?> { [_options.AuthPostureKey] = "Open" };
    }

    /// <inheritdoc />
    public RuntimeOptions Options => _options;

    /// <inheritdoc />
    public Topology Topology => Topology.Local;

    /// <summary>The local bindings, shared by local+cloud.</summary>
    public static Bindings LocalBindings { get; } = new(
        ClusteringBinding.Localhost,
        GrainStoreBinding.Memory,
        StreamsBinding.Memory,
        RemindersBinding.Memory,
        SecretsBinding.MachineKey,
        HttpBinding.Direct,
        SandboxBinding.LocalContainer,
        ModelBinding.ConfiguredEndpoint,
        UiBinding.LocalWeb)
    { ClusterStore = ClusterStoreBinding.HostBound };

    /// <inheritdoc />
    public Bindings Bindings => LocalBindings;

    /// <summary>The local process shape with the <see cref="RuntimeOptions.IntoChatCompatible"/> environment names.</summary>
    public static Services LocalServices { get; } = ServicesFor(RuntimeOptions.IntoChatCompatible);

    /// <summary>The local process shape for <paramref name="options"/>, shared by local+cloud. Equal options give the same instance.</summary>
    public static Services ServicesFor(RuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return ServicesByOptions.GetOrAdd(options, static o => Build(o));
    }

    private static Services Build(RuntimeOptions options)
        => new(
            [
                new ServiceProcess("os", options.OsImage, [TopologyContext.DefaultSiloPort, TopologyContext.DefaultGatewayPort, HttpPort], ["<Home>"],
                    [EnvironmentName(options.TopologyKey), EnvironmentName(options.HomeKey)]),
                new ServiceProcess("os-sandbox", options.SandboxImage, [], [], []),
            ],
            []);

    /// <inheritdoc />
    public Services Services { get; }

    /// <summary>
    /// Below <c>&lt;Home&gt;/settings.json</c>. A local run defaults auth posture to Open, as the AppHost does.
    /// local+cloud shares this dictionary. CloudProfile leaves the auth posture key required.
    /// </summary>
    public IReadOnlyDictionary<string, string?> Defaults { get; }

    /// <summary>The folder of one file grain store.</summary>
    public static string StoreDirectory(StateHome home, string name)
    {
        ArgumentNullException.ThrowIfNull(home);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Path.Combine(home.State, "files", name);
    }

    /// <inheritdoc />
    public IEnumerable<TopologyIssue> Validate(IConfiguration configuration, IReadOnlyList<ModuleManifest> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);
        foreach (var module in modules)
        {
            foreach (var need in module.Needs.Where(n => n.Kind == NeedKind.State && n.Name is not null && _reserved.Contains(n.Name)))
            {
                yield return new TopologyIssue($"module {module.Id ?? "(unnamed)"}",
                    $"grain store {need.Name} is reserved by the runtime; choose another name.");
            }
        }
    }

    /// <inheritdoc />
    public void ConfigureConfiguration(IConfigurationBuilder builder, TopologyContext context)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(context);
        builder.Add(new MachineKeyConfigurationSource(context.Home, context.Configuration, options: _options));
    }

    /// <inheritdoc />
    public void ConfigureSilo(ISiloBuilder silo, TopologyContext context)
    {
        ArgumentNullException.ThrowIfNull(silo);
        ArgumentNullException.ThrowIfNull(context);
        // Loopback only; the bind key widens the HTTP listener (os BindPolicy), never the silo.
        silo.UseLocalhostClustering(context.SiloPort, context.GatewayPort);

        var stores = new List<string> { _options.PlatformGrainStorage, _options.PubSubStore };
        stores.AddRange(context.Modules.SelectMany(m => m.Needs)
            .Where(n => n.Kind == NeedKind.State && n.Name is not null && n.Name != _options.ClusterGrainStorage && !_reserved.Contains(n.Name))
            .Select(n => n.Name!));
        foreach (var name in stores.Distinct(StringComparer.Ordinal))
        {
            silo.Services.AddKeyedSingleton<IGrainStorage>(name, (_, _) => new LocalMemoryGrainStorage(context.Home.State, name));
        }
        // Replaced by the host-bound registration, which ConfigureTopologySilo runs after this.
        silo.Services.AddKeyedSingleton<IGrainStorage>(_options.ClusterGrainStorage, new UnregisteredClusterStorage());

        silo.AddReminders();
        silo.Services.AddSingleton<IReminderTable>(_ => MemoryReminderTable.For(context.Home.State));
        silo.AddMemoryStreams(_options.SignalStreams);
    }

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, TopologyContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Home.EnsureCreated();
    }

    private static string EnvironmentName(string key) => key.Replace(":", "__", StringComparison.Ordinal);
}
