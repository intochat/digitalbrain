namespace DigitalBrain.Behaviors.Manifest;

using System.Text.Json.Serialization;

public sealed record BehaviorEntryPoints(
    IReadOnlyList<string> EventAliases,
    BehaviorContractManifest Contract)
{
    // Absent means no emit rights. Omitted from the canonical byte stream when null so every
    // artifact signed before broadcast emit grants existed still verifies unchanged.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? BroadcastEmitAliases { get; init; }
}
