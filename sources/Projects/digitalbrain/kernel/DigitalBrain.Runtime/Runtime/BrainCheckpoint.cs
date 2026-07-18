namespace DigitalBrain.Runtime.Runtime;

[GenerateSerializer]
public sealed class BrainCheckpoint
{
    [Id(0)] public Guid Id { get; set; }
    [Id(1)] public DateTimeOffset Timestamp { get; set; }
    [Id(2)] public string? Description { get; set; }
    [Id(3)] public Dictionary<string, byte[]> EncryptedNeuronStates { get; set; } = new(StringComparer.Ordinal);
}
