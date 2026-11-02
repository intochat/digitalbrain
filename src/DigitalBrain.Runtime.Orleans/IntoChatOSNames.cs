namespace DigitalBrain.Runtime.Orleans;

/// <summary>The <c>IntoChatOS:</c> spellings, kept for hosts that have not moved to <see cref="RuntimeOptions"/>; the strings are those of <see cref="RuntimeOptions.IntoChatCompatible"/>.</summary>
[Obsolete("Use RuntimeOptions.IntoChatCompatible; removed in 0.13.")]
public static class IntoChatOSNames
{
    /// <summary>Selects the topology: local, local+cloud or cloud.</summary>
    public const string TopologyKey = RuntimeOptions.IntoChatTopologyKey;

    /// <summary>Where local state lives; unset means ~/.intochat.</summary>
    public const string HomeKey = RuntimeOptions.IntoChatHomeKey;

    /// <summary>Widens binding beyond loopback on local and local+cloud.</summary>
    public const string BindKey = RuntimeOptions.IntoChatBindKey;

    /// <summary>The cloud storage account (cloud only).</summary>
    public const string CloudStorageKey = RuntimeOptions.IntoChatCloudStorageKey;

    /// <summary>The cloud key vault (cloud only).</summary>
    public const string CloudKeyVaultKey = RuntimeOptions.IntoChatCloudKeyVaultKey;

    /// <summary>The server a local+cloud host connects to.</summary>
    public const string CloudServerAddressKey = RuntimeOptions.IntoChatCloudServerAddressKey;

    /// <summary>The master key.</summary>
    public const string MasterKeyKey = RuntimeOptions.IntoChatMasterKeyKey;

    /// <summary>The prefix of every cloud key.</summary>
    public const string CloudSectionPrefix = RuntimeOptions.IntoChatCloudSectionPrefix;

    /// <summary>The stream pub-sub grain store.</summary>
    public const string PubSubStore = "PubSubStore";

    /// <summary>The signal stream namespace.</summary>
    public const string SignalStreams = "signals";

    /// <summary>The platform grain store.</summary>
    public const string PlatformGrainStorage = "platform";

    /// <summary>The cluster grain store.</summary>
    public const string ClusterGrainStorage = "cluster";

    /// <summary>The grain state container.</summary>
    public const string GrainStateContainer = "os-state";

    /// <summary>Signalling configuration sections.</summary>
    public static class Signalling
    {
        /// <summary>The signalling section: session lifetime and caps (04-13).</summary>
        public const string Section = RuntimeOptions.IntoChatSignallingSection;

        /// <summary>The retention section (04 D-11).</summary>
        public const string Retention = RuntimeOptions.IntoChatRetentionSection;
    }
}
