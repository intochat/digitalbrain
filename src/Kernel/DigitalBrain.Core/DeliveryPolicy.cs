namespace DigitalBrain.Core;

public static class DeliveryPolicy
{
    private const string DepthKey = "db.depth";

    public const int MaximumDepth = 16;

    public const int MaximumAttempts = 1000;

    public static readonly TimeSpan RetryHorizon = TimeSpan.FromMinutes(30);

    public static readonly TimeSpan DeliveryAttemptTimeout = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan InnerDeliveryReadBound = DeliveryAttemptTimeout - TimeSpan.FromSeconds(5);

    public static readonly TimeSpan RouteLookupTimeout = InnerDeliveryReadBound;

    public static int InboundDepth() => RequestContext.Get(DepthKey) is int depth ? depth : 0;

    public static void CarryDepth(int depth) => RequestContext.Set(DepthKey, depth);
}
