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

    // A handler racing an inner call against the outer attempt deadline must win that race to ever
    // see its own typed refusal: TryDeliverAsync arms attemptCts.CancelAfter(DeliveryAttemptTimeout)
    // before the handler's turn starts, so an inner bound equal to DeliveryAttemptTimeout always
    // loses to the outer cancellation and surfaces OperationCanceledException, never TimeoutException.
    internal static readonly TimeSpan InnerDeliveryReadBound = DeliveryAttemptTimeout - TimeSpan.FromSeconds(5);

    // Bound on both directions of subscription-registry traffic: the lookup every aliased
    // broadcast performs inside the emitting turn, and the publish every activation performs.
    internal static readonly TimeSpan SubscriptionRegistryTimeout = TimeSpan.FromSeconds(5);

    internal static int InboundDepth() => RequestContext.Get(DepthKey) is int depth ? depth : 0;

    internal static void CarryDepth(int depth) => RequestContext.Set(DepthKey, depth);
}
