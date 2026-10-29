namespace DigitalBrain.Runtime.Orleans;

/// <summary>How the cluster finds its members.</summary>
public enum ClusteringBinding
{
    /// <summary>Not bound.</summary>
    None,

    /// <summary>A single localhost member.</summary>
    Localhost,

    /// <summary>Azure Table membership.</summary>
    AzureTable,
}

/// <summary>Where named grain stores live.</summary>
public enum GrainStoreBinding
{
    /// <summary>Not bound.</summary>
    None,

    /// <summary>Files under <c>&lt;Home&gt;/state</c>.</summary>
    File,

    /// <summary>Azure Blob storage.</summary>
    AzureBlob,
}

/// <summary>How streams are carried.</summary>
public enum StreamsBinding
{
    /// <summary>Not bound.</summary>
    None,

    /// <summary>Memory streams with a file pub-sub store.</summary>
    MemoryWithFilePubSub,

    /// <summary>Azure Queue streams.</summary>
    AzureQueue,
}

/// <summary>Where durable reminders live.</summary>
public enum RemindersBinding
{
    /// <summary>Not bound.</summary>
    None,

    /// <summary>A file reminder table.</summary>
    File,

    /// <summary>Azure Table reminders.</summary>
    AzureTable,
}

/// <summary>Where secrets live.</summary>
public enum SecretsBinding
{
    /// <summary>Not bound.</summary>
    None,

    /// <summary>Encrypted under a machine key in <c>&lt;Home&gt;/secrets</c>.</summary>
    MachineKey,

    /// <summary>Azure Key Vault.</summary>
    KeyVault,
}

/// <summary>Outbound HTTP.</summary>
public enum HttpBinding
{
    /// <summary>Not bound.</summary>
    None,

    /// <summary>Direct outbound calls.</summary>
    Direct,
}

/// <summary>Where code sandboxes run.</summary>
public enum SandboxBinding
{
    /// <summary>Not bound.</summary>
    None,

    /// <summary>A container on this machine.</summary>
    LocalContainer,

    /// <summary>A hosted container.</summary>
    HostedContainer,
}

/// <summary>Which language model endpoint is used.</summary>
public enum ModelBinding
{
    /// <summary>Not bound.</summary>
    None,

    /// <summary>The endpoint named in configuration.</summary>
    ConfiguredEndpoint,
}

/// <summary>Where the UI is served.</summary>
public enum UiBinding
{
    /// <summary>Not bound.</summary>
    None,

    /// <summary>A local web server.</summary>
    LocalWeb,

    /// <summary>A hosted web front.</summary>
    HostedWeb,
}

/// <summary>Who provides the <c>cluster</c> grain store.</summary>
public enum ClusterStoreBinding
{
    /// <summary>Not bound.</summary>
    None,

    /// <summary>The host supplies it through <see cref="ClusterStoreHosting"/> (os registers BrainLog this way until it moves into the runtime).</summary>
    HostBound,
}

/// <summary>What a topology binds each resource kind to. A kind whose binding is <c>None</c> is unbound.</summary>
public sealed record Bindings(
    ClusteringBinding Clustering,
    GrainStoreBinding GrainStores,
    StreamsBinding Streams,
    RemindersBinding Reminders,
    SecretsBinding Secrets,
    HttpBinding Http,
    SandboxBinding Sandbox,
    ModelBinding Model,
    UiBinding Ui)
{
    /// <summary>Who provides the <c>cluster</c> grain store; <see cref="GrainStores"/> covers every other named store.</summary>
    public ClusterStoreBinding ClusterStore { get; init; }

    /// <summary>Nothing bound.</summary>
    public static Bindings Unbound { get; } = new(default, default, default, default, default, default, default, default, default);

    /// <summary>True when a module need of this kind is bound.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A need kind without a mapping.</exception>
    public bool Binds(NeedKind kind) => kind switch
    {
        NeedKind.State => GrainStores != GrainStoreBinding.None,
        NeedKind.Streams => Streams != StreamsBinding.None,
        NeedKind.Reminders => Reminders != RemindersBinding.None,
        NeedKind.Secrets => Secrets != SecretsBinding.None,
        NeedKind.Http => Http != HttpBinding.None,
        NeedKind.Sandbox => Sandbox != SandboxBinding.None,
        NeedKind.Model => Model != ModelBinding.None,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "This need kind has no binding."),
    };

    /// <summary>
    /// True when <paramref name="need"/> is bound. A <c>State("cluster")</c> need is bound only when
    /// <see cref="ClusterStore"/> is <see cref="ClusterStoreBinding.HostBound"/> and the host registered the store.
    /// </summary>
    public bool Binds(Need need, bool clusterStoreRegistered)
    {
        ArgumentNullException.ThrowIfNull(need);
        return IsClusterStore(need)
            ? ClusterStore == ClusterStoreBinding.HostBound && clusterStoreRegistered
            : Binds(need.Kind);
    }

    internal static bool IsClusterStore(Need need)
        => need.Kind == NeedKind.State && need.Name == IntoChatOSNames.ClusterGrainStorage;
}
