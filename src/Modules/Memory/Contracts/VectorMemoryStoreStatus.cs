namespace DigitalBrain.Memory;

[GenerateSerializer]
[Alias("memory.vector-store-status")]
public enum VectorMemoryStoreStatus
{
    Stored = 0,
    ReservedNamespace = 1,
}
