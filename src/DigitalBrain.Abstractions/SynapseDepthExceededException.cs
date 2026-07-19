using Orleans;

namespace DigitalBrain;

[GenerateSerializer]
[Alias("db.depth-error")]
public sealed class SynapseDepthExceededException : Exception
{
    public SynapseDepthExceededException()
        : this("A synapse chain exceeded the maximum recursion depth.")
    {
    }

    public SynapseDepthExceededException(string message)
        : base(message)
    {
    }

    public SynapseDepthExceededException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
