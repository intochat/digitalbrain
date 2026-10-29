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
public sealed class LocalProfile : ITopologyProfile
{
    /// <summary>The os HTTP port (os Kernel launch settings).</summary>
    public const int HttpPort = 5310;

    /// <summary>The os container image.</summary>
    public const string Image = "ghcr.io/intochat/os";

    /// <summary>The sandbox container image.</summary>
    public const string SandboxImage = "ghcr.io/intochat/os-sandbox";

    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        IntoChatOSNames.PlatformGrainStorage,
        IntoChatOSNames.PubSubStore,
    };

    /// <inheritdoc />
    public Topology Topology => Topology.Local;

    /// <summary>The local bindings, shared by local+cloud.</summary>
    public static Bindings LocalBindings { get; } = new(
        ClusteringBinding.Localhost,
        GrainStoreBinding.File,
        StreamsBinding.MemoryWithFilePubSub,
        RemindersBinding.File,
        SecretsBinding.MachineKey,
        HttpBinding.Direct,
        SandboxBinding.LocalContainer,
        ModelBinding.ConfiguredEndpoint,
        UiBinding.LocalWeb)
    { ClusterStore = ClusterStoreBinding.HostBound };

    /// <inheritdoc />
    public Bindings Bindings => LocalBindings;

    /// <summary>The local process shape, shared by local+cloud.</summary>
    public static Services LocalServices { get; } = new(
        [
            new ServiceProcess("os", Image, [TopologyContext.DefaultSiloPort, TopologyContext.DefaultGatewayPort, HttpPort], ["<Home>"],
                [EnvironmentName(IntoChatOSNames.TopologyKey), EnvironmentName(IntoChatOSNames.HomeKey)]),
            new ServiceProcess("os-sandbox", SandboxImage, [], [], []),
        ],
        []);

    /// <inheritdoc />
    public Services Services => LocalServices;

    /// <summary>
    /// Below <c>&lt;Home&gt;/settings.json</c>. A local run defaults auth posture to Open, as the AppHost does.
    /// local+cloud shares this dictionary. CloudProfile leaves <c>IntoChatOS:Auth:Posture</c> required.
    /// </summary>
    public IReadOnlyDictionary<string, string?> Defaults { get; } = new Dictionary<string, string?>
    {
        ["IntoChatOS:Auth:Posture"] = "Open",
    };

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
            foreach (var need in module.Needs.Where(n => n.Kind == NeedKind.State && n.Name is not null && Reserved.Contains(n.Name)))
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
        builder.Add(new MachineKeyConfigurationSource(context.Home, context.Configuration));
    }

    /// <inheritdoc />
    public void ConfigureSilo(ISiloBuilder silo, TopologyContext context)
    {
        ArgumentNullException.ThrowIfNull(silo);
        ArgumentNullException.ThrowIfNull(context);
        // Loopback only; IntoChatOS:Bind widens the HTTP listener (os BindPolicy), never the silo.
        silo.UseLocalhostClustering(context.SiloPort, context.GatewayPort);

        var stores = new List<string> { IntoChatOSNames.PlatformGrainStorage, IntoChatOSNames.PubSubStore };
        stores.AddRange(context.Modules.SelectMany(m => m.Needs)
            .Where(n => n.Kind == NeedKind.State && n.Name is not null && n.Name != IntoChatOSNames.ClusterGrainStorage && !Reserved.Contains(n.Name))
            .Select(n => n.Name!));
        foreach (var name in stores.Distinct(StringComparer.Ordinal))
        {
            silo.AddFileGrainStorage(name, options => options.RootDirectory = StoreDirectory(context.Home, name));
        }
        // Replaced by the host-bound registration, which ConfigureTopologySilo runs after this.
        silo.Services.AddKeyedSingleton<IGrainStorage>(IntoChatOSNames.ClusterGrainStorage, new UnregisteredClusterStorage());

        silo.AddReminders();
        silo.Services.AddSingleton<IReminderTable>(_ => new FileReminderTable(context.Home));
        silo.AddMemoryStreams(IntoChatOSNames.SignalStreams);
    }

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, TopologyContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Home.EnsureCreated();
    }

    private static string EnvironmentName(string key) => key.Replace(":", "__", StringComparison.Ordinal);
}
