namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.route-outcome-kind")]
public enum RouteOutcomeKind
{
    Delivered,
    Refused,
    Failed,
    Abandoned,
    Unrouted,
    Expired,
    Disabled,
}

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
    [property: Id(5)] CorrelationId Correlation) : Synapse
{
    public const int MaximumReasonLength = 2048;

    public static RouteOutcome For(
        SynapseDelivery delivery,
        string alias,
        NeuronId receiver,
        RouteOutcomeKind kind,
        string reason)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        return new(
            delivery.SynapseId,
            alias,
            receiver,
            kind,
            Shortened(reason),
            delivery.CorrelationId);
    }

    // An unbounded reason pushes a reader's cursor past the journal's retention window.
    private static string Shortened(string reason)
        => string.IsNullOrEmpty(reason)
            ? string.Empty
            : reason.Length <= MaximumReasonLength ? reason : reason[..MaximumReasonLength];
}
