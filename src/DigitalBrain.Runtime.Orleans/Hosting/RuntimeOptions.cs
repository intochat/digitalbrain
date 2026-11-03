using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Runtime.Orleans;

/// <summary>
/// The configuration keys, store names and default home folder the runtime uses. The host fills it once; the runtime reads it
/// from the service collection, falling back to <see cref="Default"/>.
/// </summary>
public sealed record RuntimeOptions
{
    /// <summary>Selects the topology: local, local+cloud or cloud.</summary>
    public string TopologyKey { get; init; } = "DigitalBrain:Topology";

    /// <summary>Where local state lives; unset means <c>~/</c><see cref="DefaultHomeDirectoryName"/>.</summary>
    public string HomeKey { get; init; } = "DigitalBrain:Home";

    /// <summary>Widens binding beyond loopback on local and local+cloud.</summary>
    public string BindKey { get; init; } = "DigitalBrain:Bind";

    /// <summary>The cloud storage account (cloud only).</summary>
    public string CloudStorageKey { get; init; } = "DigitalBrain:Cloud:Storage";

    /// <summary>The cloud key vault (cloud only).</summary>
    public string CloudKeyVaultKey { get; init; } = "DigitalBrain:Cloud:KeyVault";

    /// <summary>The server a local+cloud host connects to.</summary>
    public string CloudServerAddressKey { get; init; } = "DigitalBrain:Cloud:ServerAddress";

    /// <summary>The master key.</summary>
    public string MasterKeyKey { get; init; } = "DigitalBrain:MasterKey";

    /// <summary>The prefix of every cloud key.</summary>
    public string CloudSectionPrefix { get; init; } = "DigitalBrain:Cloud:";

    /// <summary>The signalling section: session lifetime and caps.</summary>
    public string SignallingSection { get; init; } = "DigitalBrain:Signalling";

    /// <summary>The retention section.</summary>
    public string RetentionSection { get; init; } = "DigitalBrain:Signalling:Retention";

    /// <summary>The folder name under the user folder that holds local state when <see cref="HomeKey"/> is unset.</summary>
    public string DefaultHomeDirectoryName { get; init; } = ".digitalbrain";

    /// <summary>The key a local run defaults to <c>Open</c>; cloud topologies leave it required.</summary>
    public string AuthPostureKey { get; init; } = "DigitalBrain:Auth:Posture";

    /// <summary>The container image of the host process, named by <c>Services</c>.</summary>
    public string? OsImage { get; init; }

    /// <summary>The container image of the sandbox process, named by <c>Services</c>.</summary>
    public string? SandboxImage { get; init; }

    /// <summary>The stream pub-sub grain store.</summary>
    public string PubSubStore { get; init; } = "PubSubStore";

    /// <summary>The signal stream namespace.</summary>
    public string SignalStreams { get; init; } = "signals";

    /// <summary>The platform grain store.</summary>
    public string PlatformGrainStorage { get; init; } = "platform";

    /// <summary>The cluster grain store.</summary>
    public string ClusterGrainStorage { get; init; } = "cluster";

    /// <summary>The grain state container.</summary>
    public string GrainStateContainer { get; init; } = "brain-state";

    /// <summary>Keys named after DigitalBrain, home <c>~/.digitalbrain</c>.</summary>
    public static RuntimeOptions Default { get; } = new();

    /// <summary>The options the host registered, or <see cref="Default"/> when it registered none.</summary>
    public static RuntimeOptions Resolve(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.GetService<RuntimeOptions>() ?? Default;
    }

    internal static RuntimeOptions From(IServiceProvider services) => Resolve(services);

    internal static RuntimeOptions From(IServiceCollection services)
        => services.LastOrDefault(d => d.ServiceType == typeof(RuntimeOptions) && !d.IsKeyedService)?.ImplementationInstance as RuntimeOptions
            ?? Default;
}
