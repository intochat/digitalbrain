using Orleans.Runtime;

namespace DigitalBrain;

internal readonly struct SynapseDepth : IDisposable
{
    private const string RequestContextKey = "db.depth";

    internal const int Maximum = 16;

    private SynapseDepth(int restored) => Restored = restored;

    private int Restored { get; }

    internal static SynapseDepth Enter(SynapseMetadata metadata)
    {
        var current = RequestContext.Get(RequestContextKey) is int depth ? depth : 0;
        var next = current + 1;

        if (next > Maximum)
        {
            throw new SynapseDepthExceededException(
                $"The synapse chain for correlation {metadata.CorrelationId} exceeded the maximum depth of {Maximum}.");
        }

        RequestContext.Set(RequestContextKey, next);

        return new SynapseDepth(current);
    }

    public void Dispose() => RequestContext.Set(RequestContextKey, Restored);
}
