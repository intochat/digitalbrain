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

