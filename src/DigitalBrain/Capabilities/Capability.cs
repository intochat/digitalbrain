namespace DigitalBrain.Capabilities;

/// <summary>
/// The capabilities the platform ships contracts for. <c>V1Id</c> is the literal manifests and analyzers compare against;
/// <c>V1</c> is the typed id.
/// </summary>
public static class Capability
{
    /// <summary>Compute accounting: costs, funding, budgets and external-call metering.</summary>
    public static class Compute
    {
        /// <summary>The string form of <see cref="V1"/>.</summary>
        public const string V1Id = "compute@1";

        /// <summary>compute@1.</summary>
        public static CapabilityId V1 { get; } = CapabilityId.Parse(V1Id);
    }

    /// <summary>Connectors, integrations, connections, connection requests, capability broker bindings and secret sealing.</summary>
    public static class Connections
    {
        /// <summary>The string form of <see cref="V1"/>.</summary>
        public const string V1Id = "connections@1";

        /// <summary>connections@1.</summary>
        public static CapabilityId V1 { get; } = CapabilityId.Parse(V1Id);
    }

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

    /// <summary>Read-only HTTP against the allowed hosts of a connection.</summary>
    public static class Http
    {
        /// <summary>The string form of <see cref="V1"/>.</summary>
        public const string V1Id = "http@1";

        /// <summary>http@1.</summary>
        public static CapabilityId V1 { get; } = CapabilityId.Parse(V1Id);
    }

    /// <summary>Named tools of a brain's connectors.</summary>
    public static class Tools
    {
        /// <summary>The string form of <see cref="V1"/>.</summary>
        public const string V1Id = "tool@1";

        /// <summary>tool@1.</summary>
        public static CapabilityId V1 { get; } = CapabilityId.Parse(V1Id);
    }
}
