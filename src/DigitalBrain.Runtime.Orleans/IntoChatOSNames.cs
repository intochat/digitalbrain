namespace DigitalBrain.Runtime.Orleans;

/// <summary>Configuration keys and store names shared by the runtime and os (same strings as os, P2-D-21, P2-D-22).</summary>
public static class IntoChatOSNames
{
    /// <summary>Selects the topology: local, local+cloud or cloud.</summary>
    public const string TopologyKey = "IntoChatOS:Topology";

    /// <summary>Where local state lives; unset means ~/.intochat.</summary>
    public const string HomeKey = "IntoChatOS:Home";

    /// <summary>Widens binding beyond loopback on local and local+cloud.</summary>
    public const string BindKey = "IntoChatOS:Bind";

    /// <summary>The cloud storage account (cloud only).</summary>
    public const string CloudStorageKey = "IntoChatOS:Cloud:Storage";

    /// <summary>The cloud key vault (cloud only).</summary>
    public const string CloudKeyVaultKey = "IntoChatOS:Cloud:KeyVault";

    /// <summary>The server a local+cloud host connects to.</summary>
    public const string CloudServerAddressKey = "IntoChatOS:Cloud:ServerAddress";

    /// <summary>The master key.</summary>
    public const string MasterKeyKey = "IntoChatOS:MasterKey";

    /// <summary>The prefix of every cloud key.</summary>
    public const string CloudSectionPrefix = "IntoChatOS:Cloud:";

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
        public const string Section = "IntoChatOS:Signalling";

        /// <summary>The retention section (04 D-11).</summary>
        public const string Retention = "IntoChatOS:Signalling:Retention";
    }
}
