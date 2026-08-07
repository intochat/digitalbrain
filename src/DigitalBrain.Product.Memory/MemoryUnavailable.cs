namespace DigitalBrain.Product.Memory;

public sealed record MemoryUnavailable : Synapse
{
    public const string TemporaryUnavailableMessage = "Memory is temporarily unavailable.";

    public MemoryUnavailable(string operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        Operation = operation.Trim();
        Message = TemporaryUnavailableMessage;
    }

    public string Operation { get; }

    public string Message { get; }
}
