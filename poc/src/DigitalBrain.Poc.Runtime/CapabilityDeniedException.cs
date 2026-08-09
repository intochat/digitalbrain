namespace DigitalBrain.Poc.Runtime;

public sealed class CapabilityDeniedException : Exception
{
    public CapabilityDeniedException(Type contractType)
        : base($"The candidate invocation is not granted output contract '{contractType.FullName}'.")
    {
    }

    public CapabilityDeniedException(string message)
        : base(message)
    {
    }

    public CapabilityDeniedException(Type contractType, string targetScope)
        : this(contractType)
    {
        TargetScope = targetScope;
    }

    public string? TargetScope { get; }
}
