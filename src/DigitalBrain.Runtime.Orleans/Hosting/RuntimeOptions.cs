using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Runtime.Orleans;

/// <summary>
/// The configuration keys, store names and default home folder the runtime uses. The host fills it once; the runtime reads it
/// from the service collection. <see cref="Default"/> names the keys after DigitalBrain; <see cref="IntoChatCompatible"/> keeps the
/// spellings of the <c>IntoChatOS:</c> hosts.
/// </summary>
public sealed record RuntimeOptions
{
    internal const string IntoChatTopologyKey = "IntoChatOS:Topology";
    internal const string IntoChatHomeKey = "IntoChatOS:Home";
    internal const string IntoChatBindKey = "IntoChatOS:Bind";
    internal const string IntoChatCloudStorageKey = "IntoChatOS:Cloud:Storage";
    internal const string IntoChatCloudKeyVaultKey = "IntoChatOS:Cloud:KeyVault";
    internal const string IntoChatCloudServerAddressKey = "IntoChatOS:Cloud:ServerAddress";
    internal const string IntoChatMasterKeyKey = "IntoChatOS:MasterKey";
    internal const string IntoChatCloudSectionPrefix = "IntoChatOS:Cloud:";
    internal const string IntoChatSignallingSection = "IntoChatOS:Signalling";
    internal const string IntoChatRetentionSection = "IntoChatOS:Signalling:Retention";
    internal const string IntoChatDefaultHomeDirectoryName = ".intochat";
    internal const string IntoChatAuthPostureKey = "IntoChatOS:Auth:Posture";
    internal const string IntoChatOsImage = "ghcr.io/intochat/os";
    internal const string IntoChatSandboxImage = "ghcr.io/intochat/os-sandbox";

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
    public string OsImage { get; init; } = "digitalbrain/host";

    /// <summary>The container image of the sandbox process, named by <c>Services</c>.</summary>
    public string SandboxImage { get; init; } = "digitalbrain/host-sandbox";

    /// <summary>The stream pub-sub grain store.</summary>
    public string PubSubStore { get; init; } = "PubSubStore";

    /// <summary>The signal stream namespace.</summary>
    public string SignalStreams { get; init; } = "signals";

    /// <summary>The platform grain store.</summary>
    public string PlatformGrainStorage { get; init; } = "platform";

    /// <summary>The cluster grain store.</summary>
    public string ClusterGrainStorage { get; init; } = "cluster";

    /// <summary>The grain state container.</summary>
    public string GrainStateContainer { get; init; } = "os-state";

    /// <summary>Keys named after DigitalBrain, home <c>~/.digitalbrain</c>.</summary>
    public static RuntimeOptions Default { get; } = new();

    /// <summary>The key spellings and home <c>~/.intochat</c> that the original product's hosts have always used.</summary>
    public static RuntimeOptions IntoChatCompatible { get; } = new()
    {
        TopologyKey = IntoChatTopologyKey,
        HomeKey = IntoChatHomeKey,
        BindKey = IntoChatBindKey,
        CloudStorageKey = IntoChatCloudStorageKey,
        CloudKeyVaultKey = IntoChatCloudKeyVaultKey,
        CloudServerAddressKey = IntoChatCloudServerAddressKey,
        MasterKeyKey = IntoChatMasterKeyKey,
        CloudSectionPrefix = IntoChatCloudSectionPrefix,
        SignallingSection = IntoChatSignallingSection,
        RetentionSection = IntoChatRetentionSection,
        DefaultHomeDirectoryName = IntoChatDefaultHomeDirectoryName,
        AuthPostureKey = IntoChatAuthPostureKey,
        OsImage = IntoChatOsImage,
        SandboxImage = IntoChatSandboxImage,
    };

    // The options the host registered, or the compatible spellings that callers without options have always got.
    internal static RuntimeOptions From(IServiceProvider services)
        => services.GetService<RuntimeOptions>() ?? IntoChatCompatible;

    internal static RuntimeOptions From(IServiceCollection services)
        => services.LastOrDefault(d => d.ServiceType == typeof(RuntimeOptions) && !d.IsKeyedService)?.ImplementationInstance as RuntimeOptions
            ?? IntoChatCompatible;
}
