namespace DigitalBrain.Kernel;

internal static class DeliveryPolicy
{
    private const string DepthKey = "db.depth";

    internal const int MaximumDepth = 16;

    internal const int MaximumAttempts = 1000;

    internal static readonly TimeSpan RetryHorizon = TimeSpan.FromMinutes(30);

    // Finite bound for a single outbox Deliver attempt so reminder-driven drains always pass a
    // cancelable lifecycle token that can actually fire (not merely CanBeCanceled forever).
    internal static readonly TimeSpan DeliveryAttemptTimeout = TimeSpan.FromSeconds(30);

    // Bound on the subscriber lookup every aliased broadcast performs inside the emitting turn.
    internal static readonly TimeSpan SubscriberLookupTimeout = TimeSpan.FromSeconds(5);

    internal static int InboundDepth() => RequestContext.Get(DepthKey) is int depth ? depth : 0;

    internal static void CarryDepth(int depth) => RequestContext.Set(DepthKey, depth);
}
