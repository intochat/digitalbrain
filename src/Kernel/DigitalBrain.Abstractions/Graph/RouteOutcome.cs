namespace DigitalBrain.Abstractions.Graph;

// Correlation is carried in the payload because the outcome is journaled under its own
// envelope: readers match on this field, never on the envelope's CorrelationId.
[GenerateSerializer]
[Alias("db.route-outcome")]
public sealed record RouteOutcome(
    [property: Id(0)] SynapseId Delivery,
    [property: Id(1)] string Alias,
    [property: Id(2)] NeuronId Receiver,
    [property: Id(3)] RouteOutcomeKind Kind,
    [property: Id(4)] string Reason,
    [property: Id(5)] CorrelationId Correlation,
    [property: Id(6)] string FixPath) : Synapse
{
    public const int MaximumReasonLength = 2048;
    public const int MaximumFixPathLength = 1024;

    public static RouteOutcome For(
        SynapseDelivery delivery,
        string alias,
        NeuronId receiver,
        RouteOutcomeKind kind,
        string reason,
        string? fixPath = null)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        return new(
            delivery.SynapseId,
            alias,
            receiver,
            kind,
            Shortened(reason, MaximumReasonLength),
            delivery.CorrelationId,
            Shortened(fixPath ?? SuggestFixPath(kind), MaximumFixPathLength));
    }

    public static string SuggestFixPath(RouteOutcomeKind kind)
        => kind switch
        {
            RouteOutcomeKind.Refused =>
                "Correct authorization, grants, or Connect endpoints for the verified principal, then retry.",
            RouteOutcomeKind.Abandoned =>
                "Check the target is registered/enabled and reachable, then retry. If it keeps failing, inspect depth/retry horizon.",
            RouteOutcomeKind.Unrouted =>
                "Wire a receiver with db.connect (or Send to a concrete NeuronId), then retry the emission.",
            RouteOutcomeKind.Failed =>
                "Inspect the failure reason, fix the handler or payload, then retry.",
            RouteOutcomeKind.Expired =>
                "Renew the offer or lease, then retry.",
            RouteOutcomeKind.Disabled =>
                "Enable the instance (registry) or reconnect the edge, then retry.",
            _ => "Inspect the outcome reason, fix the configuration, then retry.",
        };

    private static string Shortened(string text, int max)
        => string.IsNullOrEmpty(text)
            ? string.Empty
            : text.Length <= max ? text : text[..max];
}
