namespace DigitalBrain.Capabilities;

/// <summary>
/// The capabilities the platform ships contracts for. <c>V1Id</c> is the literal manifests and analyzers compare against;
/// <c>V1</c> is the typed id. There is no compute capability in 0.7.0.
/// </summary>
public static class Capability
{
    /// <summary>Files and folders in a storage connection.</summary>
    public static class Storage
    {
        /// <summary>The string form of <see cref="V1"/>.</summary>
        public const string V1Id = "storage@1";

        /// <summary>storage@1.</summary>
        public static CapabilityId V1 { get; } = CapabilityId.Parse(V1Id);
    }

    /// <summary>Named secrets of a brain.</summary>
    public static class Secrets
    {
        /// <summary>The string form of <see cref="V1"/>.</summary>
        public const string V1Id = "secrets@1";

        /// <summary>secrets@1.</summary>
        public static CapabilityId V1 { get; } = CapabilityId.Parse(V1Id);
    }

    /// <summary>Accounts, brains, principals, membership and grants.</summary>
    public static class Identity
    {
        /// <summary>The string form of <see cref="V1"/>.</summary>
        public const string V1Id = "identity@1";

        /// <summary>identity@1.</summary>
        public static CapabilityId V1 { get; } = CapabilityId.Parse(V1Id);
    }
}
