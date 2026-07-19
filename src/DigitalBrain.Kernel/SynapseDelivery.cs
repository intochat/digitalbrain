using Orleans.Runtime;

namespace DigitalBrain;

internal static class SynapseDelivery
{
    private const string DepthKey = "db.depth";

    internal const int MaximumDepth = 16;

    internal const int MaximumAttempts = 8;

    internal static readonly TimeSpan RetryHorizon = TimeSpan.FromMinutes(30);

    internal static int InboundDepth() => RequestContext.Get(DepthKey) is int depth ? depth : 0;

    internal static void CarryDepth(int depth) => RequestContext.Set(DepthKey, depth);
}
