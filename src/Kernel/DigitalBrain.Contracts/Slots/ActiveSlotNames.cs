namespace DigitalBrain.Abstractions.Slots;

// The kernel, the Aspire host and the gateway all address one row; these are the only names they share.
public static class ActiveSlotNames
{
    public const string SlotKey = "DigitalBrain:Slot";
    public const string ClusterIdKey = "Orleans:ClusterId";

    // The silo's Basic gate and the slot probe that has to get past it live in assemblies that cannot see
    // each other, so the one credential they share is spelled out here, where both already look.
    public const string AuthUsernameKey = "DigitalBrain:Auth:Username";
    public const string AuthPasswordKey = "DigitalBrain:Auth:Password";

    public const string Table = "DigitalBrainLeases";
    public const string PartitionKey = "slot";
    public const string RowKey = "active";
    public const string Owner = "Owner";

    // How often a silo re-reads the row. A promotion cannot switch the gateway before the standby has
    // had one of these, so the two sides read the same number.
    public static TimeSpan RefreshInterval { get; } = TimeSpan.FromSeconds(2);
}
